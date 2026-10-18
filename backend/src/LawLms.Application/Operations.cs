using LawLms.Domain;

namespace LawLms.Application;

public record Actor(Guid Id, UserRole Role)
{
    public bool IsHead => Role is UserRole.DepartmentHead or UserRole.Admin;
}
public record AllowanceRequest(Guid StudentId, int ExtraAttempts, DateTime? ExtendedUntil, string Reason);
public record OfferingSettings(string Term, DateTime StartsAt, DateTime EndsAt, decimal PassingGrade, int RequiredProgress, int AppealDays, int Revision);
public record WorkflowAction(string Action, string Reason, int Revision);
public record OfferingView(Guid Id, Guid CourseId, string Code, string Name, string CourseTitle, string AcademicYear, string Term, OfferingStatus Status, DateTime? StartsAt, DateTime? EndsAt, decimal PassingGrade, int RequiredProgress, int AppealDays, GradebookStatus GradeStatus, DateTime? GradesPublishedAt, int Revision, int StudentCount, int Progress, IReadOnlyList<Guid> CompletedLessonIds, bool CanSubmitGrades = false);
public record RosterRow(Guid Id, Guid UserId, string FullName, string Email, EnrollmentStatus Status, int Progress);
public record EnrollmentChange(Guid UserId, bool Active, string Reason);
public record QuizReviewQuestion(string Content, QuestionType Type, decimal Points, IReadOnlyList<string> Options, string GradingGuide);
public record ManagedQuiz(Guid Id, Guid TeachingClassId, string Title, QuizStatus Status, decimal Weight, decimal MultipleChoiceWeight, decimal EssayWeight, int Revision, DateTime? OpensAt, DateTime? ClosesAt, int Questions, bool ShuffleQuestions = false, bool ShuffleOptions = false);
public record QuizDraft(QuizSetup Setup, int Revision);
public record QuizSetup(Guid TeachingClassId, string Title, string Description, int DurationMinutes, int AttemptsAllowed, decimal PassingScore, DateTime OpensAt, DateTime ClosesAt, decimal Weight, IReadOnlyList<CreateQuizQuestionCommand> Questions, decimal MultipleChoiceWeight = 50, decimal EssayWeight = 50, bool ShuffleQuestions = false, bool ShuffleOptions = false);
public record AttemptView(Guid Id, Guid QuizId, DateTime StartedAt, DateTime ExpiresAt, DateTime ServerNow, DateTime? SubmittedAt, AttemptStatus Status, int Revision, IReadOnlyList<QuizQuestionDto> Questions, IReadOnlyList<SubmitQuizAnswerCommand> Answers);
public record SaveAttempt(int Revision, IReadOnlyList<SubmitQuizAnswerCommand> Answers);
public record GradeAnswer(Guid QuestionId, decimal Score, string Feedback);
public record GradeAttempt(int Revision, IReadOnlyList<GradeAnswer> Answers);
public record AnswerForGrading(Guid QuestionId, string Content, QuestionType Type, decimal MaxPoints, string Answer, decimal? Score, string Feedback, string GradingGuide);
public record AttemptForGrading(Guid Id, Guid QuizId, Guid StudentId, string StudentName, string QuizTitle, AttemptStatus Status, int Revision, decimal? FinalScore, IReadOnlyList<AnswerForGrading> Answers);
public record AssessmentGrade(Guid QuizId, string Title, decimal Weight, Guid? AttemptId, decimal? Score, decimal Maximum, bool Pending);
public record GradebookRow(Guid StudentId, string StudentName, int Progress, decimal Grade, bool Passed, IReadOnlyList<AssessmentGrade> Assessments);
public record GradebookView(Guid OfferingId, GradebookStatus Status, int Revision, bool Visible, DateTime? PublishedAt, IReadOnlyList<GradebookRow> Rows);
public record AppealRequest(Guid AttemptId, string Reason);
public record AppealView(Guid Id, Guid StudentId, string StudentName, Guid AttemptId, string Reason, string Resolution, Guid? ReviewerId, AppealStatus Status, int Revision, string ProposedGradesJson);
public record AppealDecision(string Action, string Reason, Guid? ReviewerId, int Revision, IReadOnlyList<GradeAnswer>? Answers);
public record AccountView(Guid Id, string FullName, string Email, UserRole Role, bool IsActive, bool MustChangePassword);
public record CreateAccount(string FullName, string Email, UserRole Role, string? ClassCode);
public record PasswordChange(string CurrentPassword, string NewPassword);
public record InitialCredential(string Email, string TemporaryPassword);
public interface IOperationsService
{
    Task<IReadOnlyList<OfferingView>> OfferingsAsync(Actor actor, CancellationToken ct);
    Task ConfigureAsync(Guid id, OfferingSettings settings, Actor actor, CancellationToken ct);
    Task TransitionAsync(Guid id, WorkflowAction command, Actor actor, CancellationToken ct);
    Task<IReadOnlyList<RosterRow>> RosterAsync(Guid id, Actor actor, CancellationToken ct);
    Task SyncRosterAsync(Guid id, string reason, Actor actor, CancellationToken ct);
    Task ChangeEnrollmentAsync(Guid id, EnrollmentChange command, Actor actor, CancellationToken ct);
    Task CompleteLessonAsync(Guid id, Guid lessonId, Actor actor, CancellationToken ct);
    Task<IReadOnlyList<ManagedQuiz>> QuizzesAsync(Guid id, Actor actor, CancellationToken ct);
    Task<IReadOnlyList<QuizReviewQuestion>> PreviewQuizAsync(Guid id, Actor actor, CancellationToken ct);
    Task<QuizDraft> GetQuizDraftAsync(Guid id, Actor actor, CancellationToken ct);
    Task ReviseQuizAsync(Guid id, QuizDraft command, Actor actor, CancellationToken ct);
    Task RemoveQuizDraftAsync(Guid id, WorkflowAction command, Actor actor, CancellationToken ct);
    Task<Guid> CreateQuizAsync(QuizSetup command, Actor actor, CancellationToken ct);
    Task ReviewQuizAsync(Guid id, WorkflowAction command, Actor actor, CancellationToken ct);
    Task<AttemptView> StartAsync(Guid quizId, Actor actor, CancellationToken ct);
    Task<AttemptView> SaveAsync(Guid attemptId, SaveAttempt command, bool submit, Actor actor, CancellationToken ct);
    Task SetAllowanceAsync(Guid quizId, AllowanceRequest command, Actor actor, CancellationToken ct);
    Task GenerateNotificationsAsync(CancellationToken ct);
    Task<IReadOnlyList<Notification>> NotificationsAsync(Actor actor, CancellationToken ct);
    Task ReadNotificationAsync(Guid id, Actor actor, CancellationToken ct);
    Task ExpireAttemptsAsync(CancellationToken ct);
    Task<IReadOnlyList<AttemptForGrading>> GradingAsync(Guid id, Actor actor, CancellationToken ct);
    Task<AttemptForGrading> ResultAsync(Guid id, Actor actor, CancellationToken ct);
    Task GradeAsync(Guid id, GradeAttempt command, Actor actor, CancellationToken ct);
    Task<GradebookView> GradebookAsync(Guid id, Actor actor, CancellationToken ct);
    Task GradebookActionAsync(Guid id, WorkflowAction command, Actor actor, CancellationToken ct);
    Task<IReadOnlyList<AppealView>> AppealsAsync(Guid id, Actor actor, CancellationToken ct);
    Task CreateAppealAsync(Guid id, AppealRequest command, Actor actor, CancellationToken ct);
    Task ResolveAppealAsync(Guid id, AppealDecision command, Actor actor, CancellationToken ct);
    Task<IReadOnlyList<AuditEntry>> AuditAsync(Guid id, Actor actor, CancellationToken ct);
    Task<string> ExportAsync(Guid id, Actor actor, CancellationToken ct);
    Task<IReadOnlyList<AccountView>> AccountsAsync(Actor actor, CancellationToken ct);
    Task<InitialCredential> CreateAccountAsync(CreateAccount command, Actor actor, CancellationToken ct);
    Task<InitialCredential> ResetPasswordAsync(Guid id, Actor actor, CancellationToken ct);
    Task SetAccountActiveAsync(Guid id, bool active, Actor actor, CancellationToken ct);
    Task<AuthResult> ChangePasswordAsync(PasswordChange command, Actor actor, CancellationToken ct);
}

public static class AssessmentRules
{
    public static DateTime Deadline(DateTime started, int durationMinutes, DateTime closes) => started.AddMinutes(durationMinutes) < closes ? started.AddMinutes(durationMinutes) : closes;
    public static decimal Score(QuestionType type, string answer, string correct, decimal points)
    {
        static string Normalize(string value) => value.Trim().ToLowerInvariant();
        if (type == QuestionType.MultipleChoice)
        {
            try
            {
                var actual = System.Text.Json.JsonSerializer.Deserialize<string[]>(answer) ?? [];
                var expected = System.Text.Json.JsonSerializer.Deserialize<string[]>(correct) ?? [];
                return expected.Length > 0 && actual.Select(Normalize).ToHashSet().SetEquals(expected.Select(Normalize)) ? points : 0;
            }
            catch (System.Text.Json.JsonException) { return 0; }
        }
        return Normalize(answer) == Normalize(correct) ? points : 0;
    }
    public static decimal WeightedQuestionTotal(IEnumerable<QuizQuestion> questions, IEnumerable<QuizAnswer> answers, decimal multipleChoiceWeight, decimal essayWeight)
    {
        var answerByQuestion = answers.ToDictionary(x => x.QuestionId);
        var rows = questions.Select(q => (q.Type, q.Points, Score: answerByQuestion.TryGetValue(q.Id, out var answer) ? answer.Score ?? 0 : 0m)).ToList();
        var multipleMaximum = rows.Where(x => x.Type != QuestionType.Essay).Sum(x => x.Points);
        var essayMaximum = rows.Where(x => x.Type == QuestionType.Essay).Sum(x => x.Points);
        var activeWeight = (multipleMaximum > 0 ? multipleChoiceWeight : 0) + (essayMaximum > 0 ? essayWeight : 0);
        if (activeWeight <= 0) return 0;
        var fraction = 0m;
        if (multipleMaximum > 0) fraction += rows.Where(x => x.Type != QuestionType.Essay).Sum(x => x.Score) / multipleMaximum * multipleChoiceWeight;
        if (essayMaximum > 0) fraction += rows.Where(x => x.Type == QuestionType.Essay).Sum(x => x.Score) / essayMaximum * essayWeight;
        return Math.Round(rows.Sum(x => x.Points) * fraction / activeWeight, 2, MidpointRounding.AwayFromZero);
    }
    public static decimal Total(IEnumerable<AssessmentGrade> grades) => Math.Round(grades.Sum(x => x.Maximum > 0 ? (x.Score ?? 0) / x.Maximum * 10 * x.Weight / 100 : 0), 2, MidpointRounding.AwayFromZero);
}
