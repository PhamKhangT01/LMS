namespace LawLms.Domain;

public enum UserRole { Admin, DepartmentHead, Lecturer, Student }
public enum CourseStatus { Draft, PendingReview, Published }
public record CourseReviewEvent(Guid ActorId, string Action, string Reason, DateTime At);

public enum QuizStatus { Draft, Published, PendingReview, Closed }
public enum OfferingStatus { Draft, Ready, Active, Grading, Completed, Archived }
public enum EnrollmentStatus { Active, Withdrawn, Completed }
public enum AttemptStatus { InProgress, Submitted, Graded }
public enum GradebookStatus { Draft, PendingApproval, Approved, Published, Locked }
public enum AppealStatus { Pending, Resolved, Rejected, Reviewed }
public enum QuestionType { SingleChoice, MultipleChoice, TrueFalse, Essay }
public enum ScheduleEventType { ClassSession, Exam }
public enum AttendanceStatus { Present, Absent, Excused }

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Student;
    public string? ClassCode { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public int TokenVersion { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Course> CoursesTaught { get; set; } = new List<Course>();
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
}

public class Course
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Color { get; set; } = "#355C42";
    public bool IsPublished { get; set; }
    public CourseStatus Status { get; set; }
    public int Revision { get; set; }
    public string ReviewHistoryJson { get; set; } = "[]";
    public Guid LecturerId { get; set; }
    public AppUser? Lecturer { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
}

public class Lesson
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseId { get; set; }
    public Course? Course { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int Position { get; set; }
    public int DurationMinutes { get; set; }
}

public class Enrollment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public Guid CourseId { get; set; }
    public Course? Course { get; set; }
    public int ProgressPercent { get; set; }
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
}

public class TeachingClass
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public Guid CourseId { get; set; }
    public string AcademicYear { get; set; } = string.Empty;
    public string Term { get; set; } = "HK1";
    public OfferingStatus Status { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public decimal PassingGrade { get; set; } = 5;
    public int RequiredProgress { get; set; } = 100;
    public int AppealDays { get; set; } = 7;
    public GradebookStatus GradeStatus { get; set; }
    public DateTime? GradesPublishedAt { get; set; }
    public int Revision { get; set; }
    public Guid PrimaryLecturerId { get; set; }
    public Guid AssistantLecturerOneId { get; set; }
    public Guid AssistantLecturerTwoId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}

public class ScheduleEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeachingClassId { get; set; }
    public ScheduleEventType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Room { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public bool IsCancelled { get; set; }
    public int Revision { get; set; }
    public Guid CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ScheduleChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScheduleEventId { get; set; }
    public Guid ActorId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string SnapshotJson { get; set; } = "{}";
    public DateTime At { get; set; } = DateTime.UtcNow;
}

public class AttendanceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScheduleEventId { get; set; }
    public Guid StudentId { get; set; }
    public AttendanceStatus Status { get; set; }
    public Guid MarkedById { get; set; }
    public DateTime MarkedAt { get; set; } = DateTime.UtcNow;
}

public class ForumTopic
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeachingClassId { get; set; }
    public Guid AuthorId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public int Revision { get; set; } = 1;
}

public class ForumReply
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TopicId { get; set; }
    public Guid AuthorId { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsPinnedAnswer { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ForumSubscription
{
    public Guid TopicId { get; set; }
    public Guid UserId { get; set; }
    public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;
}

public class LibraryResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public Guid UploadedById { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

public class Quiz
{
    public bool ShuffleQuestions { get; set; }
    public bool ShuffleOptions { get; set; }
    public Guid? TeachingClassId { get; set; }
    public decimal Weight { get; set; } = 100;
    public decimal MultipleChoiceWeight { get; set; } = 50;
    public decimal EssayWeight { get; set; } = 50;
    public int Revision { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseId { get; set; }
    public string ClassCode { get; set; } = string.Empty;
    public Guid CreatedById { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public int AttemptsAllowed { get; set; } = 1;
    public decimal PassingScore { get; set; }
    public DateTime? OpensAt { get; set; }
    public DateTime? ClosesAt { get; set; }
    public QuizStatus Status { get; set; } = QuizStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<QuizQuestion> Questions { get; set; } = new List<QuizQuestion>();
}

public class QuizQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuizId { get; set; }
    public Quiz? Quiz { get; set; }
    public string Content { get; set; } = string.Empty;
    public QuestionType Type { get; set; }
    public string OptionsJson { get; set; } = "[]";
    public string CorrectAnswer { get; set; } = string.Empty;
    public decimal Points { get; set; }
    public int Position { get; set; }
}

public class QuizAttempt
{
    public string QuestionOrderJson { get; set; } = "[]";
    public string OptionOrderJson { get; set; } = "{}";
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuizId { get; set; }
    public Guid StudentId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public AttemptStatus Status { get; set; }
    public int Revision { get; set; }
    public decimal AutoScore { get; set; }
    public decimal? FinalScore { get; set; }
    public ICollection<QuizAnswer> Answers { get; set; } = new List<QuizAnswer>();
}

public class QuizAnswer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AttemptId { get; set; }
    public Guid QuestionId { get; set; }
    public string Answer { get; set; } = string.Empty;
    public string Feedback { get; set; } = string.Empty;
    public decimal? Score { get; set; }
}

public class OfferingEnrollment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeachingClassId { get; set; }
    public Guid UserId { get; set; }
    public EnrollmentStatus Status { get; set; }
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
}
public class LessonCompletion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EnrollmentId { get; set; }
    public Guid LessonId { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}
public class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActorId { get; set; }
    public Guid EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public DateTime At { get; set; } = DateTime.UtcNow;
}
public class GradeAppeal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeachingClassId { get; set; }
    public Guid StudentId { get; set; }
    public Guid AttemptId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public string ProposedGradesJson { get; set; } = "[]";
    public Guid? ReviewerId { get; set; }
    public AppealStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int Revision { get; set; }
}

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid TeachingClassId { get; set; }
    public string DedupKey { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}
public class QuizAllowance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuizId { get; set; }
    public Guid StudentId { get; set; }
    public int ExtraAttempts { get; set; }
    public DateTime? ExtendedUntil { get; set; }
}

public enum QuestionDifficulty { Easy, Medium, Hard }
public enum BankQuestionSource { ManualEssay, Docx, ExistingQuiz }
public class BankQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseId { get; set; }
    public Guid CreatedById { get; set; }
    public string Content { get; set; } = string.Empty;
    public QuestionType Type { get; set; }
    public string OptionsJson { get; set; } = "[]";
    public string CorrectAnswer { get; set; } = string.Empty;
    public decimal Points { get; set; }
    public string Topic { get; set; } = string.Empty;
    public QuestionDifficulty Difficulty { get; set; } = QuestionDifficulty.Medium;
    public BankQuestionSource Source { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public int Revision { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
