using LawLms.Domain;

namespace LawLms.Application;

public sealed class AuthService(IUserRepository users, IPasswordService passwords, ITokenService tokens) : IAuthService
{
    public async Task<AuthResult> LoginAsync(LoginCommand command, CancellationToken ct)
    {
        var user = await users.GetByEmailAsync(command.Email.Trim().ToLowerInvariant(), ct);
        if (user is null || !user.IsActive || !passwords.Verify(user, command.Password)) throw new UnauthorizedAppException("Email hoặc mật khẩu không chính xác.");
        return new AuthResult(tokens.Create(user), new UserDto(user.Id, user.FullName, user.Email, user.Role.ToString(), user.MustChangePassword));
    }
}

public sealed class CourseService(ICourseRepository courses, ITeachingClassRepository teachingClasses, IEnrollmentRepository enrollments, IQuizRepository quizzes, IUnitOfWork uow) : ICourseService
{
    public async Task<IReadOnlyList<CourseSummary>> GetPublishedAsync(CancellationToken ct) => (await courses.GetPublishedAsync(ct)).Select(MapSummary).ToList();
    public async Task<IReadOnlyList<CourseSummary>> GetTeachingAsync(Guid lecturerId, CancellationToken ct)
    {
        var authored = await courses.GetForLecturerAsync(lecturerId, ct);
        var assignments = await teachingClasses.GetForLecturerAsync(lecturerId, ct);
        var assigned = await courses.GetByIdsAsync(assignments.Select(x => x.CourseId).Where(x => x != Guid.Empty).Distinct().ToArray(), ct);
        return authored.Concat(assigned).GroupBy(x => x.Id).Select(x => MapSummary(x.First())).ToList();
    }
    public async Task<IReadOnlyList<CourseSummary>> GetManagementAsync(CancellationToken ct) => (await courses.GetAllAsync(ct)).Select(MapSummary).ToList();
    public async Task<CourseDetail> GetAsync(Guid id, Guid actorId, UserRole role, CancellationToken ct)
    {
        var course = await GetCourse(id, ct);
        if (role is UserRole.Admin or UserRole.DepartmentHead) return MapDetail(course);
        if (role == UserRole.Lecturer && (course.LecturerId == actorId || (await teachingClasses.GetForLecturerAsync(actorId, ct)).Any(x => x.CourseId == id))) return MapDetail(course);
        if (role != UserRole.Student || !course.IsPublished || !await enrollments.ExistsAsync(actorId, id, ct))
            throw new UnauthorizedAppException("Bạn không có quyền đọc nội dung học phần này.");
        return MapDetail(course) with { ReviewHistory = [] };
    }
    public async Task ReviewAsync(Guid id, Guid actorId, UserRole role, string action, string reason, int revision, CancellationToken ct)
    {
        var course = await GetCourse(id, ct);
        if (course.Revision != revision) throw new ConflictException("Học phần đã thay đổi. Hãy tải lại trước khi xử lý.");
        var manager = role is UserRole.Admin or UserRole.DepartmentHead;
        var assigned = role == UserRole.Lecturer && (course.LecturerId == actorId || (await teachingClasses.GetForLecturerAsync(actorId, ct)).Any(x => x.CourseId == id));
        if (!manager && !assigned) throw new UnauthorizedAppException("Bạn không được xử lý học phần này.");
        switch (action)
        {
            case "submit" when course.Status == CourseStatus.Draft:
                if (course.Lessons.Count == 0) throw new ConflictException("Cần ít nhất một bài học trước khi gửi duyệt.");
                course.Status = CourseStatus.PendingReview; break;
            case "publish" when manager && course.Status == CourseStatus.PendingReview:
                course.Status = CourseStatus.Published; course.IsPublished = true; break;
            case "reject" when manager && course.Status == CourseStatus.PendingReview:
                if (string.IsNullOrWhiteSpace(reason)) throw new ConflictException("Vui lòng ghi lý do yêu cầu chỉnh sửa.");
                course.Status = CourseStatus.Draft; break;
            default: throw new ConflictException("Thao tác không phù hợp quyền hoặc trạng thái hiện tại.");
        }
        var history = System.Text.Json.JsonSerializer.Deserialize<List<CourseReviewEvent>>(course.ReviewHistoryJson) ?? [];
        history.Add(new CourseReviewEvent(actorId, action, reason.Trim(), DateTime.UtcNow));
        course.ReviewHistoryJson = System.Text.Json.JsonSerializer.Serialize(history);
        course.Revision++;
        await uow.SaveChangesAsync(ct);
    }
    public async Task<CourseDetail> CloneAsync(Guid id,Guid actorId,string code,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(code) || code.Length>30) throw new ConflictException("Mã phiên bản mới không hợp lệ.");
        var original=await GetCourse(id,ct);
        var copy=new Course{Code=code.Trim().ToUpperInvariant(),Title=original.Title,Description=original.Description,Category=original.Category,Color=original.Color,LecturerId=actorId};
        copy.Lessons=original.Lessons.Select(x=>new Lesson{Title=x.Title,Content=x.Content,Position=x.Position,DurationMinutes=x.DurationMinutes}).ToList();
        copy.ReviewHistoryJson=System.Text.Json.JsonSerializer.Serialize(new[]{new CourseReviewEvent(actorId,"copy","Tạo phiên bản từ "+original.Code+" ("+original.Id+")",DateTime.UtcNow)});
        await courses.AddAsync(copy,ct);await uow.SaveChangesAsync(ct);return MapDetail(copy);
    }
    private static void RequireDraft(Course course)
    {
        if (course.Status != CourseStatus.Draft) throw new ConflictException("Chỉ có thể sửa bản nháp. Nội dung đang duyệt hoặc đã công bố được khóa.");
        course.Revision++;
    }
    public async Task<CourseDetail> CreateAsync(Guid lecturerId, CreateCourseCommand command, CancellationToken ct)
    {
        var course = new Course { LecturerId = lecturerId, Code = command.Code.Trim().ToUpperInvariant(), Title = command.Title.Trim(), Description = command.Description.Trim(), Category = command.Category.Trim(), Color = command.Color, IsPublished = false };
        await courses.AddAsync(course, ct); await uow.SaveChangesAsync(ct); return MapDetail(course);
    }
    public async Task<CourseDetail> UpdateAsync(Guid id, CreateCourseCommand command, CancellationToken ct)
    {
        var course = await GetCourse(id, ct);
        RequireDraft(course);
        course.Code = command.Code.Trim().ToUpperInvariant(); course.Title = command.Title.Trim(); course.Description = command.Description.Trim(); course.Category = command.Category.Trim(); course.Color = command.Color;
        await uow.SaveChangesAsync(ct); return MapDetail(course);
    }
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var course = await GetCourse(id, ct);
        if ((await teachingClasses.GetAllAsync(ct)).Any(x => x.CourseId == id)) throw new ConflictException("Không thể xóa học phần đã được phân công cho lớp.");
        if (await enrollments.AnyForCourseAsync(id, ct)) throw new ConflictException("Không thể xóa học phần đang có học viên theo học.");
        if (await quizzes.AnyForCourseAsync(id, ct)) throw new ConflictException("Không thể xóa học phần đang có bài kiểm tra.");
        courses.Remove(course); await uow.SaveChangesAsync(ct);
    }
    public async Task UpdateLessonAsync(Guid actorId,bool isManager,Guid courseId,Guid lessonId,CreateLessonCommand command,int revision,CancellationToken ct)
    {
        var course=await GetCourse(courseId,ct);
        var assigned=(await teachingClasses.GetForLecturerAsync(actorId,ct)).Any(x=>x.CourseId==courseId);
        if(!isManager && course.LecturerId!=actorId && !assigned)throw new UnauthorizedAppException("Bạn không được sửa nội dung học phần này.");
        if(course.Revision!=revision)throw new ConflictException("Học phần đã thay đổi. Hãy tải lại.");
        RequireDraft(course);var lesson=course.Lessons.SingleOrDefault(x=>x.Id==lessonId)??throw new NotFoundException("Không có bài học này.");
        lesson.Title=command.Title.Trim();lesson.Content=command.Content.Trim();lesson.DurationMinutes=command.DurationMinutes;
        await uow.SaveChangesAsync(ct);
    }
    public async Task<LessonDto> AddLessonAsync(Guid actorId, bool isAdmin, Guid courseId, CreateLessonCommand command, CancellationToken ct)
    {
        var course = await GetCourse(courseId, ct);
        var isAssigned = (await teachingClasses.GetForLecturerAsync(actorId, ct)).Any(x => x.CourseId == courseId);
        if (!isAdmin && course.LecturerId != actorId && !isAssigned) throw new UnauthorizedAppException("Bạn chỉ được sửa học phần do mình tạo hoặc được Trưởng khoa phân công.");
        RequireDraft(course);
        var lesson = new Lesson { CourseId = courseId, Title = command.Title.Trim(), Content = command.Content.Trim(), DurationMinutes = command.DurationMinutes, Position = course.Lessons.Count + 1 };
        course.Lessons.Add(lesson); await courses.AddLessonAsync(lesson, ct); await uow.SaveChangesAsync(ct); return new LessonDto(lesson.Id, lesson.Title, lesson.Content, lesson.Position, lesson.DurationMinutes);
    }
    public async Task<IReadOnlyList<LearningSummary>> GetMyLearningAsync(Guid userId, CancellationToken ct) => (await enrollments.GetForUserAsync(userId, ct)).Where(x => x.Course!.IsPublished).Select(x => new LearningSummary(x.CourseId, x.ProgressPercent, x.Course!.Title, x.Course.Code, x.Course.Color, x.Course.Lecturer!.FullName)).ToList();
    private async Task<Course> GetCourse(Guid id, CancellationToken ct) => await courses.GetDetailAsync(id, ct) ?? throw new NotFoundException("Không tìm thấy khóa học.");
    private static CourseSummary MapSummary(Course x) => new(x.Id, x.Code, x.Title, x.Description, x.Category, x.Color, x.Lecturer?.FullName ?? string.Empty, x.Lessons.Count, x.Status, x.Revision);
    private static CourseDetail MapDetail(Course x) => new(x.Id, x.Code, x.Title, x.Description, x.Category, x.Color, x.Lecturer?.FullName ?? string.Empty, x.Lessons.OrderBy(l => l.Position).Select(l => new LessonDto(l.Id, l.Title, l.Content, l.Position, l.DurationMinutes)).ToList(), x.Status, x.Revision, System.Text.Json.JsonSerializer.Deserialize<List<CourseReviewEvent>>(x.ReviewHistoryJson) ?? []);
}

public sealed class TeachingClassService(ITeachingClassRepository classes, ICourseRepository courses, IEnrollmentRepository enrollments, IUserRepository users, IUnitOfWork uow) : ITeachingClassService
{
    public async Task<IReadOnlyList<LecturerDto>> GetLecturersAsync(CancellationToken ct) => (await users.GetLecturersAsync(ct)).Select(x => new LecturerDto(x.Id, x.FullName, x.Email)).ToList();
    public async Task<TeachingClassDto> AssignAsync(AssignTeachingClassCommand command, CancellationToken ct)
    {
        var ids = new[] { command.PrimaryLecturerId, command.AssistantLecturerOneId, command.AssistantLecturerTwoId };
        if (ids.Distinct().Count() != 3) throw new ConflictException("Giảng viên chính và hai giảng viên phụ phải là ba người khác nhau.");
        var lecturers = await users.GetByIdsAsync(ids, ct);
        if (lecturers.Count != 3 || lecturers.Any(x => x.Role != UserRole.Lecturer)) throw new NotFoundException("Danh sách giảng viên không hợp lệ.");
        var course = await courses.GetDetailAsync(command.CourseId, ct) ?? throw new NotFoundException("Không tìm thấy học phần.");
        var teachingClass = new TeachingClass { Code = command.Code.Trim().ToUpperInvariant(), Name = command.Name.Trim(), CourseId = course.Id, CourseTitle = course.Title, AcademicYear = command.AcademicYear.Trim(), Term = command.Term.Trim(), PrimaryLecturerId = command.PrimaryLecturerId, AssistantLecturerOneId = command.AssistantLecturerOneId, AssistantLecturerTwoId = command.AssistantLecturerTwoId };
        await classes.AddAsync(teachingClass, ct);
        await classes.SyncRosterAsync(teachingClass, ct);
        var classStudents = await users.GetStudentsByClassCodeAsync(teachingClass.Code, ct);
        foreach (var student in classStudents)
            if (!await enrollments.ExistsAsync(student.Id, course.Id, ct)) await enrollments.AddAsync(new Enrollment { UserId = student.Id, CourseId = course.Id }, ct);
        await uow.SaveChangesAsync(ct); return Map(teachingClass, lecturers);
    }
    public async Task<TeachingClassDto> UpdateAsync(Guid id, AssignTeachingClassCommand command, CancellationToken ct)
    {
        var teachingClass = await classes.GetAsync(id, ct) ?? throw new NotFoundException("Không tìm thấy phân công giảng dạy.");
        if (teachingClass.Status != OfferingStatus.Draft) throw new ConflictException("Chỉ thay đổi phân công khi lớp ở trạng thái nháp.");
        var ids = new[] { command.PrimaryLecturerId, command.AssistantLecturerOneId, command.AssistantLecturerTwoId };
        if (ids.Distinct().Count() != 3) throw new ConflictException("Giảng viên chính và hai giảng viên phụ phải là ba người khác nhau.");
        var lecturers = await users.GetByIdsAsync(ids, ct);
        if (lecturers.Count != 3 || lecturers.Any(x => x.Role != UserRole.Lecturer)) throw new NotFoundException("Danh sách giảng viên không hợp lệ.");
        var course = await courses.GetDetailAsync(command.CourseId, ct) ?? throw new NotFoundException("Không tìm thấy học phần.");
        if (teachingClass.CourseId != command.CourseId || teachingClass.Code != command.Code.Trim().ToUpperInvariant()) throw new ConflictException("Tạo lớp học phần mới để thay đổi học phần hoặc mã lớp; giữ nguyên lịch sử lớp cũ.");
        teachingClass.Term = command.Term.Trim();
        teachingClass.Code = command.Code.Trim().ToUpperInvariant(); teachingClass.Name = command.Name.Trim(); teachingClass.CourseId = course.Id; teachingClass.CourseTitle = course.Title; teachingClass.AcademicYear = command.AcademicYear.Trim(); teachingClass.PrimaryLecturerId = command.PrimaryLecturerId; teachingClass.AssistantLecturerOneId = command.AssistantLecturerOneId; teachingClass.AssistantLecturerTwoId = command.AssistantLecturerTwoId;
        teachingClass.Revision++;
        await classes.SyncRosterAsync(teachingClass, ct);
        await uow.SaveChangesAsync(ct); return Map(teachingClass, lecturers);
    }
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var teachingClass = await classes.GetAsync(id, ct) ?? throw new NotFoundException("Không tìm thấy phân công giảng dạy.");
        if (teachingClass.Status != OfferingStatus.Draft) throw new ConflictException("Chỉ thay đổi phân công khi lớp ở trạng thái nháp.");
        classes.Remove(teachingClass); await uow.SaveChangesAsync(ct);
    }
    public async Task<IReadOnlyList<TeachingClassDto>> GetAllAsync(CancellationToken ct) { var rows = await classes.GetAllAsync(ct); return await MapAll(rows, ct); }
    public async Task<IReadOnlyList<TeachingClassDto>> GetForLecturerAsync(Guid lecturerId, CancellationToken ct) { var rows = await classes.GetForLecturerAsync(lecturerId, ct); return await MapAll(rows, ct); }
    private async Task<IReadOnlyList<TeachingClassDto>> MapAll(IReadOnlyList<TeachingClass> rows, CancellationToken ct) { var ids = rows.SelectMany(x => new[] { x.PrimaryLecturerId, x.AssistantLecturerOneId, x.AssistantLecturerTwoId }).Distinct().ToArray(); var lecturers = await users.GetByIdsAsync(ids, ct); return rows.Select(x => Map(x, lecturers)).ToList(); }
    private static TeachingClassDto Map(TeachingClass x, IReadOnlyList<AppUser> lecturers) { LecturerDto Person(Guid id) { var user = lecturers.Single(u => u.Id == id); return new LecturerDto(user.Id, user.FullName, user.Email); } return new TeachingClassDto(x.Id, x.Code, x.Name, x.CourseId, x.CourseTitle, x.AcademicYear, Person(x.PrimaryLecturerId), Person(x.AssistantLecturerOneId), Person(x.AssistantLecturerTwoId), x.AssignedAt, x.Term, x.Status); }
}

public sealed class LibraryResourceService(ILibraryResourceRepository resources, IUserRepository users, IFileStorage storage, IUnitOfWork uow) : ILibraryResourceService
{
    public async Task<LibraryResourceDto> UploadAsync(Guid uploaderId, UploadLibraryResourceCommand command, Stream content, CancellationToken ct)
    {
        var extension = Path.GetExtension(command.OriginalFileName); if (string.IsNullOrWhiteSpace(extension)) throw new ConflictException("Tệp tải lên phải có phần mở rộng hợp lệ.");
        var storedFileName = await storage.SaveAsync(content, extension, ct);
        var resource = new LibraryResource { Title = command.Title.Trim(), Description = command.Description.Trim(), Category = command.Category.Trim(), OriginalFileName = Path.GetFileName(command.OriginalFileName), StoredFileName = storedFileName, ContentType = command.ContentType, SizeBytes = command.SizeBytes, UploadedById = uploaderId };
        try
        {
            await resources.AddAsync(resource, ct);
            await uow.SaveChangesAsync(ct);
            return await MapAsync(resource, ct);
        }
        catch
        {
            await storage.DeleteAsync(storedFileName, CancellationToken.None);
            throw;
        }
    }
    public async Task<IReadOnlyList<LibraryResourceDto>> SearchAsync(string? query, CancellationToken ct)
    {
        var rows = await resources.SearchAsync(query, ct);
        if (rows.Count == 0) return [];
        // Both repositories share one scoped DbContext: load uploaders once, sequentially.
        var uploaders = (await users.GetByIdsAsync(rows.Select(x => x.UploadedById).Distinct().ToArray(), ct)).ToDictionary(x => x.Id, x => x.FullName);
        return rows.Select(x => Map(x, uploaders.GetValueOrDefault(x.UploadedById, "Không xác định"))).ToList();
    }
    public async Task<(LibraryResourceDto Resource, Stream Content)> DownloadAsync(Guid id, CancellationToken ct) { var resource = await resources.GetAsync(id, ct) ?? throw new NotFoundException("Không tìm thấy học liệu."); var content = await storage.OpenReadAsync(resource.StoredFileName, ct) ?? throw new NotFoundException("Tệp học liệu không còn tồn tại."); return (await MapAsync(resource, ct), content); }
    private async Task<LibraryResourceDto> MapAsync(LibraryResource resource, CancellationToken ct) { var user = (await users.GetByIdsAsync([resource.UploadedById], ct)).SingleOrDefault(); return Map(resource, user?.FullName ?? "Không xác định"); }
    private static LibraryResourceDto Map(LibraryResource resource, string uploadedBy) => new(resource.Id, resource.Title, resource.Description, resource.Category, resource.OriginalFileName, resource.ContentType, resource.SizeBytes, uploadedBy, resource.UploadedAt);
}

public sealed class QuizService(ICourseRepository courses, IQuizRepository quizzes, ITeachingClassRepository classes, IEnrollmentRepository enrollments, IUserRepository users, IUnitOfWork uow) : IQuizService
{
    public async Task<QuizDto> CreateAsync(Guid lecturerId, CreateQuizCommand command, CancellationToken ct)
    {
        if (command.Questions.Count == 0) throw new ConflictException("Bài kiểm tra phải có ít nhất một câu hỏi.");
        var classCode = command.ClassCode.Trim().ToUpperInvariant();
        var assignment = (await classes.GetForLecturerAsync(lecturerId, ct)).Any(x => x.CourseId == command.CourseId && x.Code == classCode);
        if (!assignment) throw new UnauthorizedAppException("Bạn chỉ được tạo bài kiểm tra cho lớp và học phần được phân công.");
        if (command.DurationMinutes is < 1 or > 600 || command.AttemptsAllowed is < 1 or > 10) throw new ConflictException("Thiết lập thời lượng hoặc số lần làm bài không hợp lệ.");
        var quiz = new Quiz { CourseId = command.CourseId, ClassCode = classCode, CreatedById = lecturerId, Title = command.Title.Trim(), Description = command.Description.Trim(), DurationMinutes = command.DurationMinutes, AttemptsAllowed = command.AttemptsAllowed, PassingScore = command.PassingScore, OpensAt = command.OpensAt, ClosesAt = command.ClosesAt };
        if (quiz.ClosesAt is not null && quiz.OpensAt is not null && quiz.ClosesAt <= quiz.OpensAt) throw new ConflictException("Thời điểm đóng phải sau thời điểm mở.");
        quiz.Questions = command.Questions.Select((x, i) => new QuizQuestion { Content = x.Content.Trim(), Type = x.Type, OptionsJson = System.Text.Json.JsonSerializer.Serialize(x.Options), CorrectAnswer = x.CorrectAnswer.Trim(), Points = x.Points, Position = i + 1 }).ToList();
        if (quiz.Questions.Any(x => string.IsNullOrWhiteSpace(x.Content) || x.Points <= 0)) throw new ConflictException("Mỗi câu hỏi phải có nội dung và điểm lớn hơn 0.");
        await quizzes.AddAsync(quiz, ct); await uow.SaveChangesAsync(ct); return Map(quiz);
    }
    public async Task PublishAsync(Guid lecturerId, bool isAdmin, Guid quizId, CancellationToken ct)
    {
        var quiz = await quizzes.GetAsync(quizId, ct) ?? throw new NotFoundException("Không tìm thấy bài kiểm tra.");
        if (!isAdmin && quiz.CreatedById != lecturerId) throw new UnauthorizedAppException("Bạn không có quyền công bố bài kiểm tra này.");
        if (quiz.Questions.Count == 0) throw new ConflictException("Không thể công bố bài kiểm tra chưa có câu hỏi.");
        quiz.Status = QuizStatus.Published; await uow.SaveChangesAsync(ct);
    }
    public async Task<IReadOnlyList<QuizDto>> GetMyAsync(Guid userId, UserRole role, CancellationToken ct)
    {
        if (role is UserRole.Lecturer or UserRole.Admin) return (await quizzes.GetForCreatorAsync(userId, ct)).Select(Map).ToList();
        var student = (await users.GetByIdsAsync([userId], ct)).SingleOrDefault() ?? throw new NotFoundException("Không tìm thấy học viên.");
        if (string.IsNullOrWhiteSpace(student.ClassCode)) return [];
        var learning = await enrollments.GetForUserAsync(userId, ct);
        var result = new List<Quiz>(); foreach (var item in learning) result.AddRange(await quizzes.GetForCourseAndClassAsync(item.CourseId, student.ClassCode, ct));
        var publishedCourseIds = learning.Where(x => x.Course!.IsPublished).Select(x => x.CourseId).ToHashSet();
        var now = DateTime.UtcNow; return result.Where(x => publishedCourseIds.Contains(x.CourseId)).Where(x => x.Status == QuizStatus.Published && (x.OpensAt is null || x.OpensAt <= now) && (x.ClosesAt is null || x.ClosesAt >= now)).Select(Map).ToList();
    }
    public async Task<QuizDetailDto> GetForStudentAsync(Guid quizId, Guid studentId, CancellationToken ct)
    {
        var quiz = await ValidateStudentAccess(quizId, studentId, ct); return new QuizDetailDto(Map(quiz), quiz.Questions.OrderBy(x => x.Position).Select(MapQuestion).ToList());
    }
    public async Task<QuizAttemptResultDto> SubmitAsync(Guid quizId, Guid studentId, SubmitQuizCommand command, CancellationToken ct)
    {
        var quiz = await ValidateStudentAccess(quizId, studentId, ct);
        if (await quizzes.AttemptCountAsync(quizId, studentId, ct) >= quiz.AttemptsAllowed) throw new ConflictException("Bạn đã dùng hết số lần làm bài.");
        var answerMap = command.Answers.GroupBy(x => x.QuestionId).ToDictionary(x => x.Key, x => x.Last().Answer.Trim());
        var attempt = new QuizAttempt { QuizId = quizId, StudentId = studentId };
        var manual = false; foreach (var question in quiz.Questions) { answerMap.TryGetValue(question.Id, out var answer); decimal? score; if (question.Type == QuestionType.Essay) { score = null; manual = true; } else score = Normalize(answer) == Normalize(question.CorrectAnswer) ? question.Points : 0; attempt.AutoScore += score ?? 0; attempt.Answers.Add(new QuizAnswer { QuestionId = question.Id, Answer = answer ?? string.Empty, Score = score }); }
        if (!manual) attempt.FinalScore = attempt.AutoScore; await quizzes.AddAttemptAsync(attempt, ct); await uow.SaveChangesAsync(ct); return new QuizAttemptResultDto(attempt.Id, attempt.AutoScore, attempt.FinalScore, manual);
    }
    private async Task<Quiz> ValidateStudentAccess(Guid quizId, Guid studentId, CancellationToken ct)
    {
        var quiz = await quizzes.GetAsync(quizId, ct) ?? throw new NotFoundException("Không tìm thấy bài kiểm tra.");
        var course = await courses.GetDetailAsync(quiz.CourseId, ct);
        if (course is null || !course.IsPublished) throw new UnauthorizedAppException("Học phần chưa được công bố.");
        var student = (await users.GetByIdsAsync([studentId], ct)).SingleOrDefault(); if (student?.Role != UserRole.Student || student.ClassCode != quiz.ClassCode) throw new UnauthorizedAppException("Bạn không thuộc lớp được phép làm bài kiểm tra này.");
        if (!await enrollments.ExistsAsync(studentId, quiz.CourseId, ct)) throw new UnauthorizedAppException("Bạn chưa được xếp vào học phần này.");
        var now = DateTime.UtcNow; if (quiz.Status != QuizStatus.Published || quiz.OpensAt > now || quiz.ClosesAt < now) throw new ConflictException("Bài kiểm tra hiện chưa mở."); return quiz;
    }
    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
    private static QuizDto Map(Quiz x) => new(x.Id, x.CourseId, x.ClassCode, x.Title, x.Description, x.DurationMinutes, x.AttemptsAllowed, x.PassingScore, x.OpensAt, x.ClosesAt, x.Status, x.Questions.Count, x.CreatedAt);
    private static QuizQuestionDto MapQuestion(QuizQuestion x) => new(x.Id, x.Content, x.Type, System.Text.Json.JsonSerializer.Deserialize<List<string>>(x.OptionsJson) ?? [], x.Points, x.Position);
}
