using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Security.Claims;
using System.Xml.Linq;
using LawLms.Application;
using LawLms.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawLms.Api.Controllers;

[ApiController, Authorize, Route("api/quizzes")]
public sealed class QuizzesController(IQuizService quizzes) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<IReadOnlyList<QuizDto>>> GetMy(CancellationToken ct) => Ok(await quizzes.GetMyAsync(UserId(), Enum.Parse<UserRole>(User.FindFirstValue(ClaimTypes.Role)!), ct));
    [Authorize(Roles = "Lecturer,Admin")]
    [HttpPost] public IActionResult Create() => Conflict(new { detail = "Dùng quy trình đánh giá theo lớp học phần tại /api/operations/quizzes." });
    [Authorize(Roles = "Lecturer,DepartmentHead,Admin")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [HttpPost("import-docx")]
    public async Task<ActionResult<IReadOnlyList<ImportedQuizQuestionDto>>> ImportDocx([FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length < 22) return ValidationProblem("Chọn tệp DOCX để nhập.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".docx", StringComparison.OrdinalIgnoreCase)) return ValidationProblem("Chỉ hỗ trợ tệp DOCX.");
        await using var source = file.OpenReadStream();
        try
        {
            using var zip = new ZipArchive(source, ZipArchiveMode.Read);
            if (zip.Entries.Count > 2000 || zip.Entries.Sum(x => x.Length) > 50 * 1024 * 1024) return ValidationProblem("Nội dung DOCX sau giải nén vượt giới hạn cho phép.");
            var document = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException();
            if (document.Length > 20 * 1024 * 1024) return ValidationProblem("Nội dung DOCX vượt giới hạn 20 MB sau giải nén.");
            await using var xml = document.Open(); var root = XDocument.Load(xml);
            XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var lines = root.Descendants(word + "p").Select(p => string.Concat(p.Descendants(word + "t").Select(t => t.Value)).Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            var result = QuizDocxParser.Parse(lines);
            if (result.Count == 0) return ValidationProblem("Không đọc được câu hỏi. Dùng mẫu: Câu 1:, A., B., ... và Đáp án: A.");
            return Ok(result);
        }
        catch (InvalidDataException) { return ValidationProblem("Tệp DOCX bị lỗi hoặc không đúng định dạng."); }
        catch (System.Xml.XmlException) { return ValidationProblem("Nội dung DOCX không phải XML hợp lệ."); }
    }
    [Authorize(Roles = "Lecturer,Admin")]
    [HttpPost("{id:guid}/publish")] public async Task<IActionResult> Publish(Guid id, CancellationToken ct) { await Task.CompletedTask; throw new ConflictException("Dùng quy trình gửi duyệt đề kiểm tra theo lớp học phần."); }
    [Authorize(Roles = "Student")]
    [HttpGet("{id:guid}")] public IActionResult Get(Guid id) => Conflict(new { detail = "Bắt đầu hoặc tiếp tục lượt làm bài trong lớp học phần." });
    [Authorize(Roles = "Student")]
    [HttpPost("{id:guid}/submit")] public async Task<ActionResult<QuizAttemptResultDto>> Submit(Guid id, SubmitQuizRequest request, CancellationToken ct) { await Task.CompletedTask; throw new ConflictException("Hãy bắt đầu lượt làm và nộp bài qua /api/operations/attempts."); }
    private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public sealed class CreateQuizRequest
{
    public Guid CourseId { get; init; }
    [Required, StringLength(30)] public string ClassCode { get; init; } = string.Empty;
    [Required, StringLength(300)] public string Title { get; init; } = string.Empty;
    [StringLength(4000)] public string? Description { get; init; }
    [Range(1, 600)] public int DurationMinutes { get; init; }
    [Range(1, 10)] public int AttemptsAllowed { get; init; } = 1;
    [Range(0, 100000)] public decimal PassingScore { get; init; }
    public DateTime? OpensAt { get; init; }
    public DateTime? ClosesAt { get; init; }
    [MinLength(1)] public IReadOnlyList<CreateQuizQuestionRequest> Questions { get; init; } = [];
}
public sealed class CreateQuizQuestionRequest { [Required, StringLength(5000)] public string Content { get; init; } = string.Empty; public QuestionType Type { get; init; } public IReadOnlyList<string>? Options { get; init; } public string? CorrectAnswer { get; init; } [Range(typeof(decimal), "0.01", "100000")] public decimal Points { get; init; } }
public sealed class SubmitQuizRequest { public IReadOnlyList<SubmitQuizAnswerRequest> Answers { get; init; } = []; }
public sealed class SubmitQuizAnswerRequest { public Guid QuestionId { get; init; } public string? Answer { get; init; } }
public sealed record ImportedQuizQuestionDto(string Content, IReadOnlyList<string> Options, string CorrectAnswer);

internal static class QuizDocxParser
{
    public static IReadOnlyList<ImportedQuizQuestionDto> Parse(IReadOnlyList<string> lines)
    {
        var rows = new List<(string Content, List<string> Options, string Answer)>(); (string Content, List<string> Options, string Answer)? current = null;
        foreach (var line in lines)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^(Câu|Question)\s*\d+\s*[:.\-]", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) { if (current is not null) rows.Add(current.Value); current = (System.Text.RegularExpressions.Regex.Replace(line, @"^(Câu|Question)\s*\d+\s*[:.\-]\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase), [], ""); continue; }
            if (current is null) continue;
            var option = System.Text.RegularExpressions.Regex.Match(line, @"^([A-D])\s*[.)]\s*(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (option.Success) { current.Value.Options.Add(option.Groups[2].Value.Trim()); continue; }
            var answer = System.Text.RegularExpressions.Regex.Match(line, @"^(Đáp án|Dap an|Answer)\s*[:\-]\s*([A-D])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (answer.Success) { var index = answer.Groups[2].Value.ToUpperInvariant()[0] - 'A'; current = (current.Value.Content, current.Value.Options, index >= 0 && index < current.Value.Options.Count ? current.Value.Options[index] : ""); }
        }
        if (current is not null) rows.Add(current.Value);
        return rows.Where(x => !string.IsNullOrWhiteSpace(x.Content) && x.Options.Count >= 2 && !string.IsNullOrWhiteSpace(x.Answer)).Select(x => new ImportedQuizQuestionDto(x.Content, x.Options, x.Answer)).ToList();
    }
}
