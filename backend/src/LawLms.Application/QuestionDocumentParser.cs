using System.Text.RegularExpressions;
using LawLms.Domain;

namespace LawLms.Application;

public record DocumentLine(int Page, string Text);
public record DocumentQuestion(int Number, int Page, string Content, QuestionType Type, IReadOnlyList<string> Options, string CorrectAnswer, IReadOnlyList<string> Warnings);

/// <summary>Conservative text recognition: retain incomplete questions, never infer an answer.</summary>
public static class QuestionDocumentParser
{
    private static readonly Regex Start = new(@"^\s*(?:Câu|Cau|Question)\s*(\d+)\s*[.:)\-]\s*(.*)$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    private static readonly Regex Option = new(@"(?:^|\s)([A-H])[.)]\s+", RegexOptions.None, TimeSpan.FromSeconds(1));
    private static readonly Regex Answer = new(@"^\s*(?:Đáp\s*án|Dap\s*an|Answer|Đáp\s*án\s*đúng)\s*[:\-]\s*(.+)$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    private sealed class Draft(int number, int page)
    {
        public int Number = number, Page = page;
        public string Content = "", Answer = "";
        public List<(char Label, string Text)> Options = [];
        public List<string> Warnings = [];
    }

    public static IReadOnlyList<DocumentQuestion> Parse(IReadOnlyList<DocumentLine> lines)
    {
        if (lines.Count > 30000 || lines.Sum(x => x.Text.Length) > 1_000_000) throw new ConflictException("Tệp quá nhiều nội dung. Hãy chia thành các phần nhỏ hơn.");
        var drafts = new List<Draft>(); Draft? current = null;
        var keys = new Dictionary<int, string>();
        var inKey = false;
        foreach (var item in lines)
        {
            var line = item.Text.Trim();
            if (line.Length == 0) continue;
            if (drafts.Count > 0 && Regex.IsMatch(line, @"^(?:BẢNG\s+)?(?:ĐÁP ÁN|DAP AN)(?:\s+(?:MÃ|ĐỀ|THAM KHẢO|CHI TIẾT).*)?\s*$", RegexOptions.IgnoreCase)) { inKey = true; current = null; continue; }
            if (inKey)
            {
                // Only unambiguous numbered letter pairs, not multi-code answer tables.
                var pairs = Regex.Matches(line, @"(?:^|\s)(\d{1,3})\s*[.:)\-]?\s+([A-H])(?=\s|$)");
                if (pairs.Count > 0 && Regex.Replace(line, @"(?:^|\s)(\d{1,3})\s*[.:)\-]?\s+([A-H])(?=\s|$)", "").Trim().Length == 0)
                    foreach (Match pair in pairs) { var n = int.Parse(pair.Groups[1].Value); if (!keys.TryAdd(n, pair.Groups[2].Value)) keys[n] = ""; }
                continue;
            }
            var start = Start.Match(line);
            if (start.Success)
            {
                if (drafts.Count >= 200) throw new ConflictException("Mỗi lần xem trước tối đa 200 câu. Hãy chia tệp.");
                current = new(int.Parse(start.Groups[1].Value), item.Page); drafts.Add(current);
                line = start.Groups[2].Value;
            }
            if (current is null) continue;
            var answer = Answer.Match(line);
            if (answer.Success) { current.Answer = answer.Groups[1].Value.Trim(); continue; }
            if (Regex.IsMatch(line, @"^(?:Trang\s+\d|Mã\s+đề|HẾT\b|[-–]{3,})", RegexOptions.IgnoreCase)) continue;
            var options = Option.Matches(line);
            if (options.Count > 0)
            {
                var prefix = line[..options[0].Index].Trim();
                if (prefix.Length > 0) current.Content += (current.Content.Length > 0 ? "\n" : "") + prefix;
                for (var i = 0; i < options.Count; i++)
                {
                    var match = options[i]; var end = i + 1 < options.Count ? options[i + 1].Index : line.Length;
                    current.Options.Add((match.Groups[1].Value[0], line[(match.Index + match.Length)..end].Trim()));
                }
            }
            else if (line.Length > 0)
            {
                if (current.Options.Count > 0)
                {
                    var last = current.Options[^1]; current.Options[^1] = (last.Label, last.Text + " " + line);
                }
                else current.Content += (current.Content.Length > 0 ? "\n" : "") + line;
            }
        }
        return drafts.Select(d =>
        {
            var type = d.Options.Count > 0 ? QuestionType.SingleChoice : QuestionType.Essay;
            var answer = d.Answer;
            if (answer.Length == 0) keys.TryGetValue(d.Number, out answer);
            var correct = "";
            if (type == QuestionType.Essay)
            {
                correct = answer ?? "";
                d.Warnings.Add("Chưa nhận diện được phương án. Kiểm tra lại dạng tự luận và nội dung trong tệp gốc.");
            }
            else
            {
                var labels = Regex.IsMatch(answer ?? "", @"^[A-H](?:\s*[,;]\s*[A-H])*[.]?$", RegexOptions.IgnoreCase)
                    ? Regex.Matches(answer!.ToUpperInvariant(), "[A-H]").Select(x => x.Value[0]).Distinct().ToArray() : [];
                if (labels.Length > 1) type = QuestionType.MultipleChoice;
                var selected = labels.Select(label => d.Options.Where(x => x.Label == label).ToArray()).ToArray();
                if (selected.Length > 0 && selected.All(x => x.Length == 1)) correct = type == QuestionType.MultipleChoice ? System.Text.Json.JsonSerializer.Serialize(selected.Select(x => x[0].Text)) : selected[0][0].Text;
                if (correct.Length == 0) d.Warnings.Add("Chưa có đáp án rõ ràng; chọn đáp án sau khi đối chiếu tệp gốc.");
                if (d.Options.Select(x => x.Label).Distinct().Count() != d.Options.Count) d.Warnings.Add("Nhãn phương án bị lặp; cần kiểm tra lại cách tách câu.");
            }
            if (drafts.Count(x => x.Number == d.Number) > 1) d.Warnings.Add("Số câu lặp lại trong tệp; kiểm tra xem tệp có nhiều mã đề hay không.");
            return new DocumentQuestion(d.Number, d.Page, d.Content.Trim(), type, d.Options.Select(x => x.Text).ToArray(), correct, d.Warnings);
        }).ToArray();
    }
}
