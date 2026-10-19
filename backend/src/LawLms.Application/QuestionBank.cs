using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LawLms.Domain;

namespace LawLms.Application;

public record BankCourse(Guid Id, string Code, string Title);
public record BankQuestionView(Guid Id, Guid CourseId, Guid CreatedById, string AuthorName, string Content, QuestionType Type, IReadOnlyList<string> Options, string CorrectAnswer, decimal Points, string Topic, QuestionDifficulty Difficulty, BankQuestionSource Source, bool IsArchived, int Revision);
public record BankFilter(Guid CourseId, string? Query = null, string? Topic = null, QuestionType? Type = null, QuestionDifficulty? Difficulty = null, bool IncludeArchived = false, int Page = 1, int PageSize = 20);
public record BankPage(IReadOnlyList<BankQuestionView> Items, int Total, int Page, int PageSize);
public record BankEssay(Guid CourseId, string Content, string GradingGuide, decimal Points, string Topic, QuestionDifficulty Difficulty);
public record BankEdit(string Content, IReadOnlyList<string> Options, string CorrectAnswer, decimal Points, string Topic, QuestionDifficulty Difficulty, int Revision);
public record BankArchive(bool Archived, int Revision);
public record BankImportResult(int Added, int Skipped);
public record BankFromQuiz(string Topic = "", QuestionDifficulty Difficulty = QuestionDifficulty.Medium);
public record BankDraw(Guid CourseId, int Count, string? Topic = null, QuestionType? Type = null, QuestionDifficulty? Difficulty = null, IReadOnlyList<Guid>? ExcludeIds = null, string? Query = null);
public interface IQuestionBankService
{
    Task<IReadOnlyList<BankCourse>> CoursesAsync(Actor actor, CancellationToken ct);
    Task<BankPage> SearchAsync(BankFilter filter, Actor actor, CancellationToken ct);
    Task<Guid> CreateEssayAsync(BankEssay command, Actor actor, CancellationToken ct);
    Task EditAsync(Guid id, BankEdit command, Actor actor, CancellationToken ct);
    Task ArchiveAsync(Guid id, BankArchive command, Actor actor, CancellationToken ct);
    Task<BankImportResult> ImportAsync(Guid courseId, string topic, QuestionDifficulty difficulty, IReadOnlyList<CreateQuizQuestionCommand> questions, BankQuestionSource source, Actor actor, CancellationToken ct);
    Task<BankImportResult> FromQuizAsync(Guid quizId, BankFromQuiz command, Actor actor, CancellationToken ct);
    Task<IReadOnlyList<BankQuestionView>> DrawAsync(BankDraw command, Actor actor, CancellationToken ct);
}

public static class BankQuestionRules
{
    public static void Validate(CreateQuizQuestionCommand q, string topic, QuestionDifficulty difficulty)
    {
        if (!Enum.IsDefined(difficulty) || topic.Length > 200 || string.IsNullOrWhiteSpace(q.Content) || q.Content.Length > 5000 || q.Points is <= 0 or > 100000 || !Enum.IsDefined(q.Type) || q.CorrectAnswer.Length > 5000)
            throw new ConflictException("Nội dung, điểm, chủ đề hoặc độ khó của câu hỏi không hợp lệ.");
        if (q.Type == QuestionType.Essay)
        {
            if (q.Options.Count != 0) throw new ConflictException("Câu tự luận không có phương án trắc nghiệm.");
            return;
        }
        if (q.Options.Count is < 2 or > 20 || q.Options.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 2000) || q.Options.Distinct(StringComparer.Ordinal).Count() != q.Options.Count)
            throw new ConflictException("Câu trắc nghiệm cần 2–20 phương án khác nhau và không để trống.");
        if (q.Type == QuestionType.MultipleChoice)
        {
            try
            {
                var correct = JsonSerializer.Deserialize<string[]>(q.CorrectAnswer) ?? [];
                if (correct.Length == 0 || correct.Distinct().Count() != correct.Length || correct.Any(x => !q.Options.Contains(x))) throw new JsonException();
            }
            catch (JsonException) { throw new ConflictException("Đáp án nhiều lựa chọn phải là mảng JSON chứa phương án hợp lệ."); }
        }
        else if (!q.Options.Contains(q.CorrectAnswer)) throw new ConflictException("Đáp án đúng phải thuộc danh sách phương án.");
    }
    public static string Fingerprint(CreateQuizQuestionCommand q) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { q.Type, content = q.Content.Trim().Replace("\r\n", "\n").Normalize(), q.Options, q.CorrectAnswer }))));
}

/// <summary>Presentation is persisted per attempt; scoring always uses the original question IDs and answer text.</summary>
public static class QuizPresentation
{
    public static void Shuffle<T>(IList<T> rows)
    {
        for (var i = rows.Count - 1; i > 0; i--) { var j = RandomNumberGenerator.GetInt32(i + 1); (rows[i], rows[j]) = (rows[j], rows[i]); }
    }
    public static void Initialize(Quiz quiz, QuizAttempt attempt)
    {
        var questions = quiz.Questions.OrderBy(x => x.Position).Select(x => x.Id).ToList();
        if (quiz.ShuffleQuestions) Shuffle(questions);
        attempt.QuestionOrderJson = JsonSerializer.Serialize(questions);
        var options = new Dictionary<Guid, List<string>>();
        foreach (var q in quiz.Questions)
        {
            var list = JsonSerializer.Deserialize<List<string>>(q.OptionsJson) ?? [];
            if (quiz.ShuffleOptions && q.Type is QuestionType.SingleChoice or QuestionType.MultipleChoice) Shuffle(list);
            options[q.Id] = list;
        }
        attempt.OptionOrderJson = JsonSerializer.Serialize(options);
    }
    public static IReadOnlyList<QuizQuestionDto> Questions(Quiz quiz, QuizAttempt attempt)
    {
        var order = JsonSerializer.Deserialize<List<Guid>>(attempt.QuestionOrderJson) ?? [];
        var options = JsonSerializer.Deserialize<Dictionary<Guid, List<string>>>(attempt.OptionOrderJson) ?? [];
        var positions = order.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        return quiz.Questions.OrderBy(q => positions.GetValueOrDefault(q.Id, order.Count + q.Position))
            .Select((q, index) => new QuizQuestionDto(q.Id, q.Content, q.Type, options.GetValueOrDefault(q.Id) ?? JsonSerializer.Deserialize<List<string>>(q.OptionsJson) ?? [], q.Points, index + 1)).ToList();
    }
}
