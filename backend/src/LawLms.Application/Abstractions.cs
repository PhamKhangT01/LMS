using LawLms.Domain;

namespace LawLms.Application;

public interface IUserRepository { Task<AppUser?> GetByEmailAsync(string email, CancellationToken ct); Task<IReadOnlyList<AppUser>> GetLecturersAsync(CancellationToken ct); Task<IReadOnlyList<AppUser>> GetStudentsByClassCodeAsync(string classCode, CancellationToken ct); Task<IReadOnlyList<AppUser>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct); }
public interface ICourseRepository { Task AddLessonAsync(Lesson lesson, CancellationToken ct); Task<IReadOnlyList<Course>> GetAllAsync(CancellationToken ct); Task<IReadOnlyList<Course>> GetPublishedAsync(CancellationToken ct); Task<IReadOnlyList<Course>> GetForLecturerAsync(Guid lecturerId, CancellationToken ct); Task<IReadOnlyList<Course>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct); Task<Course?> GetDetailAsync(Guid id, CancellationToken ct); Task AddAsync(Course course, CancellationToken ct); void Remove(Course course); }
public interface IEnrollmentRepository { Task<bool> ExistsAsync(Guid userId, Guid courseId, CancellationToken ct); Task<bool> AnyForCourseAsync(Guid courseId, CancellationToken ct); Task AddAsync(Enrollment enrollment, CancellationToken ct); Task<IReadOnlyList<Enrollment>> GetForUserAsync(Guid userId, CancellationToken ct); }
public interface ITeachingClassRepository { Task AddAsync(TeachingClass teachingClass, CancellationToken ct); Task<TeachingClass?> GetAsync(Guid id, CancellationToken ct); Task SyncRosterAsync(TeachingClass item, CancellationToken ct); Task<IReadOnlyList<TeachingClass>> GetAllAsync(CancellationToken ct); Task<IReadOnlyList<TeachingClass>> GetForLecturerAsync(Guid lecturerId, CancellationToken ct); void Remove(TeachingClass teachingClass); }
public interface ILibraryResourceRepository { Task AddAsync(LibraryResource resource, CancellationToken ct); Task<LibraryResource?> GetAsync(Guid id, CancellationToken ct); Task<IReadOnlyList<LibraryResource>> SearchAsync(string? query, CancellationToken ct); }
public interface IQuizRepository { Task AddAsync(Quiz quiz, CancellationToken ct); Task<Quiz?> GetAsync(Guid id, CancellationToken ct); Task<bool> AnyForCourseAsync(Guid courseId, CancellationToken ct); Task<IReadOnlyList<Quiz>> GetForCourseAndClassAsync(Guid courseId, string classCode, CancellationToken ct); Task<IReadOnlyList<Quiz>> GetForCreatorAsync(Guid userId, CancellationToken ct); Task<int> AttemptCountAsync(Guid quizId, Guid studentId, CancellationToken ct); Task AddAttemptAsync(QuizAttempt attempt, CancellationToken ct); }
public interface IFileStorage { Task<string> SaveAsync(Stream content, string extension, CancellationToken ct); Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken ct); Task DeleteAsync(string storedFileName, CancellationToken ct); }
public interface IUnitOfWork { Task<int> SaveChangesAsync(CancellationToken ct); }
public interface IPasswordService { bool Verify(AppUser user, string password); }
public interface ITokenService { string Create(AppUser user); }
public interface IAuthService { Task<AuthResult> LoginAsync(LoginCommand command, CancellationToken ct); }
public interface ICourseService { Task<IReadOnlyList<CourseSummary>> GetPublishedAsync(CancellationToken ct); Task<IReadOnlyList<CourseSummary>> GetTeachingAsync(Guid lecturerId, CancellationToken ct); Task<CourseDetail> GetAsync(Guid id, Guid actorId, UserRole role, CancellationToken ct); Task<CourseDetail> CloneAsync(Guid id, Guid actorId, string code, CancellationToken ct); Task<IReadOnlyList<CourseSummary>> GetManagementAsync(CancellationToken ct); Task ReviewAsync(Guid id, Guid actorId, UserRole role, string action, string reason, int revision, CancellationToken ct); Task<CourseDetail> CreateAsync(Guid lecturerId, CreateCourseCommand command, CancellationToken ct); Task<CourseDetail> UpdateAsync(Guid id, CreateCourseCommand command, CancellationToken ct); Task DeleteAsync(Guid id, CancellationToken ct); Task UpdateLessonAsync(Guid actorId, bool isManager, Guid courseId, Guid lessonId, CreateLessonCommand command, int revision, CancellationToken ct); Task<LessonDto> AddLessonAsync(Guid actorId, bool isAdmin, Guid courseId, CreateLessonCommand command, CancellationToken ct); Task<IReadOnlyList<LearningSummary>> GetMyLearningAsync(Guid userId, CancellationToken ct); }
public interface ITeachingClassService { Task<IReadOnlyList<LecturerDto>> GetLecturersAsync(CancellationToken ct); Task<TeachingClassDto> AssignAsync(AssignTeachingClassCommand command, CancellationToken ct); Task<TeachingClassDto> UpdateAsync(Guid id, AssignTeachingClassCommand command, CancellationToken ct); Task DeleteAsync(Guid id, CancellationToken ct); Task<IReadOnlyList<TeachingClassDto>> GetAllAsync(CancellationToken ct); Task<IReadOnlyList<TeachingClassDto>> GetForLecturerAsync(Guid lecturerId, CancellationToken ct); }
public interface ILibraryResourceService { Task<LibraryResourceDto> UploadAsync(Guid uploaderId, UploadLibraryResourceCommand command, Stream content, CancellationToken ct); Task<IReadOnlyList<LibraryResourceDto>> SearchAsync(string? query, CancellationToken ct); Task<(LibraryResourceDto Resource, Stream Content)> DownloadAsync(Guid id, CancellationToken ct); }
public interface IQuizService { Task<QuizDto> CreateAsync(Guid lecturerId, CreateQuizCommand command, CancellationToken ct); Task PublishAsync(Guid lecturerId, bool isAdmin, Guid quizId, CancellationToken ct); Task<IReadOnlyList<QuizDto>> GetMyAsync(Guid userId, UserRole role, CancellationToken ct); Task<QuizDetailDto> GetForStudentAsync(Guid quizId, Guid studentId, CancellationToken ct); Task<QuizAttemptResultDto> SubmitAsync(Guid quizId, Guid studentId, SubmitQuizCommand command, CancellationToken ct); }

public record LoginCommand(string Email, string Password);
public record CreateCourseCommand(string Code, string Title, string Description, string Category, string Color);
public record CreateLessonCommand(string Title, string Content, int DurationMinutes);
public record AssignTeachingClassCommand(string Code, string Name, Guid CourseId, string AcademicYear, Guid PrimaryLecturerId, Guid AssistantLecturerOneId, Guid AssistantLecturerTwoId, string Term = "HK1");
public record UploadLibraryResourceCommand(string Title, string Description, string Category, string OriginalFileName, string ContentType, long SizeBytes);
public record CreateQuizQuestionCommand(string Content, QuestionType Type, IReadOnlyList<string> Options, string CorrectAnswer, decimal Points);
public record CreateQuizCommand(Guid CourseId, string ClassCode, string Title, string Description, int DurationMinutes, int AttemptsAllowed, decimal PassingScore, DateTime? OpensAt, DateTime? ClosesAt, IReadOnlyList<CreateQuizQuestionCommand> Questions);
public record SubmitQuizAnswerCommand(Guid QuestionId, string Answer);
public record SubmitQuizCommand(IReadOnlyList<SubmitQuizAnswerCommand> Answers);
public record AuthResult(string Token, UserDto User);
public record UserDto(Guid Id, string FullName, string Email, string Role, bool MustChangePassword = false);
public record CourseSummary(Guid Id, string Code, string Title, string Description, string Category, string Color, string Lecturer, int Lessons, CourseStatus Status, int Revision);
public record LessonDto(Guid Id, string Title, string Content, int Position, int DurationMinutes);
public record CourseDetail(Guid Id, string Code, string Title, string Description, string Category, string Color, string Lecturer, IReadOnlyList<LessonDto> Lessons, CourseStatus Status, int Revision, IReadOnlyList<CourseReviewEvent> ReviewHistory);
public record LearningSummary(Guid CourseId, int ProgressPercent, string Title, string Code, string Color, string Lecturer);
public record LecturerDto(Guid Id, string FullName, string Email);
public record TeachingClassDto(Guid Id, string Code, string Name, Guid CourseId, string CourseTitle, string AcademicYear, LecturerDto PrimaryLecturer, LecturerDto AssistantLecturerOne, LecturerDto AssistantLecturerTwo, DateTime AssignedAt, string Term = "HK1", OfferingStatus Status = OfferingStatus.Draft);
public record LibraryResourceDto(Guid Id, string Title, string Description, string Category, string OriginalFileName, string ContentType, long SizeBytes, string UploadedBy, DateTime UploadedAt);
public record QuizQuestionDto(Guid Id, string Content, QuestionType Type, IReadOnlyList<string> Options, decimal Points, int Position);
public record QuizDto(Guid Id, Guid CourseId, string ClassCode, string Title, string Description, int DurationMinutes, int AttemptsAllowed, decimal PassingScore, DateTime? OpensAt, DateTime? ClosesAt, QuizStatus Status, int QuestionCount, DateTime CreatedAt);
public record QuizDetailDto(QuizDto Quiz, IReadOnlyList<QuizQuestionDto> Questions);
public record QuizAttemptResultDto(Guid AttemptId, decimal AutoScore, decimal? FinalScore, bool PendingManualGrading);

public sealed class NotFoundException(string message) : Exception(message);
public sealed class ConflictException(string message) : Exception(message);
public sealed class UnauthorizedAppException(string message) : Exception(message);
