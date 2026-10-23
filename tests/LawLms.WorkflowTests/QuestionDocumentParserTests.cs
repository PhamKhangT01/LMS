using LawLms.Application;
using LawLms.Domain;
using Xunit;

namespace LawLms.WorkflowTests;

public class QuestionDocumentParserTests
{
    private static IReadOnlyList<DocumentQuestion> Parse(params string[] lines) => QuestionDocumentParser.Parse(lines.Select((text, i) => new DocumentLine(i < 3 ? 1 : 2, text)).ToArray());
    [Fact] public void RetainsIncompleteQuestionsAndContinuation()
    {
        var rows = Parse("Câu 1: Nội dung dài", "tiếp tục ở dòng sau", "A. Một", "B. Hai", "Câu 2: Câu khác", "A. Ba B. Bốn", "Đáp án: B");
        Assert.Equal(2,rows.Count); Assert.Contains("tiếp tục",rows[0].Content);
        Assert.Empty(rows[0].CorrectAnswer); Assert.NotEmpty(rows[0].Warnings);
        Assert.Equal("Bốn",rows[1].CorrectAnswer); Assert.Equal(2, rows[1].Page);
    }
    [Fact] public void DoesNotInferUnderlinedOrBoldAnswers()
    {
        var row=Assert.Single(Parse("Câu 1: Một?", "A. Phương án có thể đúng", "B. Phương án khác"));
        Assert.Empty(row.CorrectAnswer);
    }
    [Fact] public void ReadsMultipleAnswersAndEssay()
    {
        var rows=Parse("Question 1: Chọn nhiều", "A. Một B. Hai C. Ba", "Answer: A,C", "Câu 2: Phân tích một tình huống", "Dòng tiếp theo.");
        Assert.Equal(QuestionType.MultipleChoice,rows[0].Type);Assert.Equal("[\"Một\",\"Ba\"]", System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonSerializer.Deserialize<string[]>(rows[0].CorrectAnswer),new System.Text.Json.JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
        Assert.Equal(QuestionType.Essay,rows[1].Type);Assert.Contains("Dòng tiếp theo", rows[1].Content);
    }
    [Fact] public void ReadsOnlyUnambiguousAnswerTables()
    {
        var rows=Parse("Câu 1: Một?", "A. X B. Y", "Câu 2: Hai?", "A. Z B. W", "ĐÁP ÁN", "1 B 2 A");
        Assert.Equal("Y",rows[0].CorrectAnswer);Assert.Equal("Z",rows[1].CorrectAnswer);
        rows=Parse("Câu 1: Một?", "A. X B. Y", "ĐÁP ÁN", "1 A B");Assert.Empty(rows[0].CorrectAnswer);
    }
    [Fact] public void HeaderDoesNotSwitchToAnswerTableBeforeQuestions()
    {
        var row=Assert.Single(Parse("ĐÁP ÁN ĐỀ THI KTHP", "Câu 1: Nội dung", "A. Một B. Hai"));Assert.Empty(row.CorrectAnswer);
    }
    [Fact] public void DuplicateNumberIsFlaggedAndNoRowIsDropped()
    {
        var rows=Parse("Câu 1: Nội dung", "A. Một B. Hai", "Câu 1: Mã đề thứ hai", "A. Ba B. Bốn");
        Assert.Equal(2,rows.Count);Assert.All(rows,q=>Assert.Contains(q.Warnings,w=>w.Contains("Số câu lặp")));
    }
    [Fact] public void OversizedInputIsRejected()
    {
        Assert.Throws<ConflictException>(()=>QuestionDocumentParser.Parse([new(1,new string('a',1_000_001))]));
        Assert.Throws<ConflictException>(()=>Parse(Enumerable.Range(1,201).Select(i=>$"Câu {i}: Nội dung").ToArray()));
    }
}
