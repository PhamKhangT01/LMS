using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LawLms.Application;
using LawLms.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LawLms.Infrastructure.Persistence;

/// <summary>Explicit local QA fixtures. Never called by normal application startup.</summary>
public sealed class TestDataSeeder(LmsDbContext db, string storageRoot, string password)
{
    private readonly Dictionary<string, int> added = new();
    private readonly Dictionary<string, int> retained = new();
    private readonly List<string> createdFiles = [];
    private readonly PasswordHasher<AppUser> hasher = new();
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    private CancellationToken cancellation;
    private DateTime now;
    public static Guid Id(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes("LawLms:qa:v1:" + key)).AsSpan(0, 16));

    private async Task<T> Put<T>(T row, params object[] keys) where T : class
    {
        var found = await db.FindAsync<T>(keys, cancellation);
        var counts = found is null ? added : retained;
        counts[typeof(T).Name] = counts.GetValueOrDefault(typeof(T).Name) + 1;
        if (found is not null) return found;
        db.Add(row);
        return row;
    }
    private Task<T> Put<T>(T row, string key) where T : class => Put(row, Id(key));
    private Task<AuditEntry> Log(string key, Guid actor, Guid entity, string action, object detail, DateTime at)
        => Put(new AuditEntry { Id = Id("audit:" + key), ActorId = actor, EntityId = entity, Action = action, Detail = JsonSerializer.Serialize(detail, json), At = at }, "audit:" + key);

    private async Task<AppUser> User(string key, string name, string email, UserRole role, string? classCode = null, bool active = true, bool change = false)
    {
        var id = Id("user:" + key);
        var collision = await db.Users.AsNoTracking().AnyAsync(u => u.Email == email && u.Id != id, cancellation);
        if (collision) throw new InvalidOperationException($"Email kiểm thử đã thuộc bản ghi khác: {email}. Không ghi đè.");
        var existing = await db.Users.FindAsync([id], cancellation);
        var row = new AppUser { Id = id, FullName = name + " (Kiểm thử)", Email = email, Role = role, ClassCode = classCode, IsActive = active, MustChangePassword = change, CreatedAt = now.AddDays(-240) };
        if (existing is null) row.PasswordHash = hasher.HashPassword(row, password);
        return await Put(row, id);
    }

    public async Task<object> SeedAsync(CancellationToken ct = default)
    {
        cancellation = ct;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7161032026)", ct);
        now = await db.AuditEntries.Where(a => a.Id == Id("audit:seed-created")).Select(a => (DateTime?)a.At).SingleOrDefaultAsync(ct) ?? DateTime.UtcNow;
        try
        {
            var admin = await User("admin", "Quản trị dữ liệu kiểm thử", "seed.admin@lms.test", UserRole.Admin);
            var head = await User("head", "Trưởng khoa kiểm thử", "seed.truongkhoa@lms.test", UserRole.DepartmentHead);
            string[] teacherNames = ["Nguyễn Minh An", "Lê Thu Hương", "Trần Quang Hải", "Phạm Đức Bình"];
            var teachers = new List<AppUser>();
            for (var i = 0; i < 4; i++) teachers.Add(await User($"teacher:{i}", teacherNames[i], $"seed.giangvien{i + 1}@lms.test", UserRole.Lecturer));
            string[] studentNames = ["Nguyễn Hoài Nam", "Trần Minh Châu", "Lê Ngọc Anh", "Phạm Quốc Huy", "Hoàng Thanh Hà", "Vũ Đức Long", "Đặng Thu Trang", "Bùi Minh Khang", "Đỗ Quỳnh Mai", "Ngô Hải Đăng"];
            var groups = new List<List<AppUser>>();
            for (var group = 0; group < 3; group++)
            {
                var students = new List<AppUser>();
                for (var i = 0; i < 10; i++) students.Add(await User($"student:{group}:{i}", studentNames[(i + group * 3) % 10], $"seed.hocvien{group * 10 + i + 1:00}@lms.test", UserRole.Student, $"TEST-K49A{group + 1}"));
                groups.Add(students);
            }
            await User("locked", "Học viên bị khóa", "seed.bikhoa@lms.test", UserRole.Student, "TEST-LOCK", active: false);
            await User("first-login", "Học viên đăng nhập lần đầu", "seed.doimatkhau@lms.test", UserRole.Student, "TEST-NEW", change: true);
            await User("outsider", "Học viên ngoài lớp", "seed.ngoailop@lms.test", UserRole.Student, "TEST-OUT");

            // Some legacy database foreign keys have no EF relationship metadata.
            // Flush principals in stages, within the same atomic transaction.
            await db.SaveChangesAsync(ct);

            string[] codes = ["TEST-PLDC", "TEST-HP", "TEST-DS", "TEST-HS", "TEST-HC", "TEST-TTHS", "TEST-TTDS", "TEST-QT"];
            string[] titles = ["Pháp luật đại cương", "Luật Hiến pháp", "Luật Dân sự", "Luật Hình sự", "Luật Hành chính", "Luật Tố tụng hình sự", "Luật Tố tụng dân sự", "Pháp luật quốc tế"];
            string[] lessonTitles = ["Khái quát và mục tiêu học phần", "Khái niệm và phương pháp tiếp cận", "Phân tích cấu trúc quy phạm", "Đọc và đối chiếu tài liệu", "Thảo luận tình huống", "Ôn tập và tự đánh giá"];
            var courses = new List<Course>();
            var lessons = new List<List<Lesson>>();
            for (var i = 0; i < titles.Length; i++)
            {
                var key = $"course:{i}";
                var state = i == 6 ? CourseStatus.Draft : i == 7 ? CourseStatus.PendingReview : CourseStatus.Published;
                var history = new List<CourseReviewEvent> { new(head.Id, "create", "Tạo học phần phục vụ kiểm thử", now.AddDays(-200)) };
                if (state != CourseStatus.Draft) history.Add(new(head.Id, "submit", "Gửi duyệt nội dung kiểm thử", now.AddDays(-199)));
                if (state == CourseStatus.Published) history.Add(new(head.Id, "publish", "Công bố bộ nội dung kiểm thử", now.AddDays(-198)));
                var course = await Put(new Course { Id = Id(key), Code = codes[i], Title = "[TEST] " + titles[i], Description = "Dữ liệu giả lập để kiểm thử LMS; dùng tình huống học tập, không thay thế giáo trình hoặc văn bản pháp luật chính thức.", Category = "Pháp luật", Color = i % 2 == 0 ? "#355C42" : "#456C59", LecturerId = teachers[i % 4].Id, Status = state, IsPublished = state == CourseStatus.Published, Revision = history.Count, ReviewHistoryJson = JsonSerializer.Serialize(history), CreatedAt = now.AddDays(-200) }, key);
                courses.Add(course);
                var rows = new List<Lesson>();
                for (var j = 0; j < lessonTitles.Length; j++)
                {
                    var lessonKey = $"lesson:{i}:{j}";
                    rows.Add(await Put(new Lesson { Id = Id(lessonKey), CourseId = course.Id, Title = lessonTitles[j], Position = j + 1, DurationMinutes = 30 + j * 5, Content = $"## {lessonTitles[j]}\n\nHọc phần: {titles[i]}. Đây là nội dung giả lập có chủ đích cho kiểm thử chức năng đọc bài và theo dõi tiến độ.\n\n### Mục tiêu\n- Xác định câu hỏi và phạm vi vấn đề cần nghiên cứu.\n- Tìm tài liệu được giảng viên giao và ghi rõ nguồn.\n- Trình bày lập luận, phân biệt dữ kiện và giả định.\n\n### Tình huống thảo luận\nNhóm A và nhóm B đưa ra hai cách giải quyết khác nhau cho một tình huống học tập. Hãy liệt kê dữ kiện còn thiếu, tài liệu cần đối chiếu và lập luận của mỗi nhóm.\n\n### Tự kiểm tra\nTóm tắt nội dung bằng ba ý chính, gửi câu hỏi ở diễn đàn và đánh dấu hoàn thành sau khi đọc." }, lessonKey));
                }
                lessons.Add(rows);
            }

            int[] courseIndices = [0, 1, 2, 3, 4, 0, 1, 5];
            int[] groupIndices = [2, 2, 0, 1, 0, 0, 1, 2];
            OfferingStatus[] states = [OfferingStatus.Draft, OfferingStatus.Ready, OfferingStatus.Active, OfferingStatus.Active, OfferingStatus.Grading, OfferingStatus.Grading, OfferingStatus.Completed, OfferingStatus.Archived];
            string[] labels = ["Nháp – chờ cấu hình", "Sẵn sàng mở lớp", "Đang học – tự luận và gia hạn", "Đang học – đề kết hợp", "Chờ duyệt bảng điểm", "Đã công bố – đang phúc khảo", "Đã hoàn thành", "Đã lưu trữ"];
            var offerings = new List<TeachingClass>();
            for (var i = 0; i < states.Length; i++)
            {
                var key = $"offering:{i}";
                var old = i >= 6;
                var gradeState = i == 4 ? GradebookStatus.PendingApproval : i == 5 ? GradebookStatus.Published : old ? GradebookStatus.Locked : GradebookStatus.Draft;
                var offering = await Put(new TeachingClass { Id = Id(key), CourseId = courses[courseIndices[i]].Id, Code = $"TEST-K49A{groupIndices[i] + 1}", Name = labels[i], CourseTitle = courses[courseIndices[i]].Title, AcademicYear = old ? "2025-2026" : "2026-2027", Term = old ? "HK2" : "HK1", Status = states[i], StartsAt = i == 0 ? null : now.AddDays(old ? -160 : i >= 4 ? -60 : -15), EndsAt = i == 0 ? null : now.AddDays(old ? -40 : i >= 4 ? -3 : 45), PassingGrade = 5, RequiredProgress = 100, AppealDays = 7, GradeStatus = gradeState, GradesPublishedAt = i == 5 ? now.AddDays(-2) : old ? now.AddDays(-30) : null, Revision = i + 1, PrimaryLecturerId = teachers[i % 4].Id, AssistantLecturerOneId = teachers[(i + 1) % 4].Id, AssistantLecturerTwoId = teachers[(i + 2) % 4].Id, AssignedAt = now.AddDays(old ? -170 : -70) }, key);
                offerings.Add(offering);
                await Log($"offering:{i}:create", head.Id, offering.Id, "offering.create", new { seed = true, name = labels[i] }, offering.AssignedAt);
                if (i >= 1) await Log($"offering:{i}:ready", head.Id, offering.Id, "offering.ready", new { reason = "Đã kiểm tra lịch, nội dung và danh sách" }, offering.AssignedAt.AddDays(1));
                if (i >= 2) await Log($"offering:{i}:activate", head.Id, offering.Id, "offering.activate", new { reason = "Mở lớp kiểm thử" }, offering.StartsAt!.Value);
                if (i >= 4) await Log($"offering:{i}:grading", head.Id, offering.Id, "offering.grading", new { reason = "Kết thúc đợt đánh giá" }, offering.EndsAt!.Value);
                if (i >= 4) await Log($"offering:{i}:submit", offering.PrimaryLecturerId, offering.Id, "gradebook.submit", new { reason = "Đã đối soát bảng điểm kiểm thử" }, old ? now.AddDays(-32) : now.AddDays(-3));
                if (i >= 5)
                {
                    await Log($"offering:{i}:approve", head.Id, offering.Id, "gradebook.approve", new { reason = "Đã duyệt kết quả" }, offering.GradesPublishedAt!.Value.AddHours(-1));
                    await Log($"offering:{i}:publish", head.Id, offering.Id, "gradebook.publish", new { reason = "Công bố kết quả kiểm thử" }, offering.GradesPublishedAt.Value);
                }
                if (old) await Log($"offering:{i}:complete", head.Id, offering.Id, "offering.complete", new { reason = "Đã hết hạn phúc khảo" }, now.AddDays(-20));
                if (i == 7) await Log("offering:7:archive", head.Id, offering.Id, "offering.archive", new { reason = "Lưu trữ hồ sơ kiểm thử" }, now.AddDays(-19));
                if (i == 0) continue;
                for (var j = 0; j < 10; j++)
                {
                    var student = groups[groupIndices[i]][j];
                    var withdrawn = i == 2 && j == 9;
                    var enrollmentKey = $"roster:{i}:{j}";
                    var enrollment = await Put(new OfferingEnrollment { Id = Id(enrollmentKey), TeachingClassId = offering.Id, UserId = student.Id, Status = withdrawn ? EnrollmentStatus.Withdrawn : old ? EnrollmentStatus.Completed : EnrollmentStatus.Active, EnrolledAt = offering.AssignedAt.AddDays(1) }, enrollmentKey);
                    var count = i >= 4 ? 6 : i == 1 || withdrawn ? 0 : j % 7;
                    if (i >= 2 && !withdrawn)
                    {
                        var legacyKey = $"enrollment:{courseIndices[i]}:{student.Id}";
                        await Put(new Enrollment { Id = Id(legacyKey), UserId = student.Id, CourseId = offering.CourseId, ProgressPercent = count * 100 / 6, EnrolledAt = enrollment.EnrolledAt }, legacyKey);
                    }
                    for (var k = 0; k < count; k++)
                    {
                        var completionKey = $"completion:{i}:{j}:{k}";
                        var at = i >= 4 ? offering.EndsAt!.Value.AddDays(-10 + k) : now.AddDays(j % 3 == 0 ? -12 : -3).AddHours(k);
                        await Put(new LessonCompletion { Id = Id(completionKey), EnrollmentId = enrollment.Id, LessonId = lessons[courseIndices[i]][k].Id, CompletedAt = at }, completionKey);
                        await Log(completionKey, student.Id, offering.Id, "lesson.complete", new { lessonId = lessons[courseIndices[i]][k].Id }, at);
                    }
                }
            }

            // A ready class has a roster; draft class is deliberately empty for testing sync/settings.
            for (var i = 0; i < offerings.Count; i++)
            {
                var offering = offerings[i];
                if (i == 0) continue;
                var old = i >= 4;
                for (var eventIndex = 0; eventIndex < 4; eventIndex++)
                {
                    var key = $"schedule:{i}:{eventIndex}";
                    var starts = old ? offering.EndsAt!.Value.Date.AddDays(-16 + eventIndex * 4).AddHours(1 + (i - 4) * 2) : now.Date.AddDays(i == 1 ? 3 + eventIndex * 4 : eventIndex < 2 ? -6 + eventIndex * 3 : 1 + i + (eventIndex - 2) * 7).AddHours(1 + i % 4 * 2);
                    var cancelled = i == 2 && eventIndex == 3;
                    var item = await Put(new ScheduleEvent { Id = Id(key), TeachingClassId = offering.Id, Type = eventIndex == 2 ? ScheduleEventType.Exam : ScheduleEventType.ClassSession, Title = (eventIndex == 2 ? "Kiểm tra học phần" : "Buổi học " + (eventIndex + 1)) + " · " + offering.CourseTitle, Room = "TEST-A" + (200 + i), Location = "Khu giảng đường kiểm thử", Notes = cancelled ? "Buổi học được hủy để kiểm tra giao diện lịch." : "Lịch giả lập; không phải kế hoạch đào tạo chính thức.", StartsAt = starts, EndsAt = starts.AddHours(2), IsCancelled = cancelled, Revision = cancelled ? 2 : 1, CreatedById = head.Id, CreatedAt = offering.AssignedAt.AddDays(2), UpdatedAt = now.AddDays(-1) }, key);
                    await Put(new ScheduleChange { Id = Id(key + ":created"), ScheduleEventId = item.Id, ActorId = head.Id, Action = "created", SnapshotJson = JsonSerializer.Serialize(new { item.Title, item.StartsAt, item.EndsAt, item.Room }, json), At = item.CreatedAt }, key + ":created");
                    if (cancelled) await Put(new ScheduleChange { Id = Id(key + ":cancelled"), ScheduleEventId = item.Id, ActorId = head.Id, Action = "cancelled", SnapshotJson = "{\"reason\":\"Hủy buổi học kiểm thử\"}", At = now.AddDays(-1) }, key + ":cancelled");
                    if (item.StartsAt > now || cancelled || item.Type == ScheduleEventType.Exam) continue;
                    for (var j = 0; j < 10; j++)
                    {
                        if (i == 2 && j == 9) continue;
                        var attendanceKey = key + ":attendance:" + j;
                        await Put(new AttendanceRecord { Id = Id(attendanceKey), ScheduleEventId = item.Id, StudentId = groups[groupIndices[i]][j].Id, Status = j % 6 == 0 ? AttendanceStatus.Absent : j % 7 == 0 ? AttendanceStatus.Excused : AttendanceStatus.Present, MarkedById = offering.PrimaryLecturerId, MarkedAt = item.EndsAt }, attendanceKey);
                    }
                }
            }

            var quizRows = new List<(Quiz Quiz, List<QuizQuestion> Questions, int Offering, string Key)>();
            for (var i = 0; i < offerings.Count; i++)
            {
                var offering = offerings[i];
                var modes = i == 2 ? new[] { "essay", "choice" } : new[] { i is 0 or 4 ? "essay" : i is 1 or 6 ? "choice" : "mixed" };
                foreach (var mode in modes)
                {
                    var key = $"quiz:{i}:{mode}";
                    var status = i == 0 ? QuizStatus.Draft : i == 1 ? QuizStatus.PendingReview : i == 2 && mode == "choice" || i >= 4 ? QuizStatus.Closed : QuizStatus.Published;
                    var closes = status == QuizStatus.Closed ? (i >= 4 ? offering.EndsAt!.Value.AddDays(-1) : now.AddDays(-2)) : now.AddDays(14);
                    var opens = status == QuizStatus.Closed ? closes.AddDays(-7) : now.AddDays(-1);
                    var quiz = await Put(new Quiz { Id = Id(key), TeachingClassId = offering.Id, CourseId = offering.CourseId, ClassCode = offering.Code, CreatedById = offering.PrimaryLecturerId, Title = "[TEST] " + (mode == "essay" ? "Tự luận tình huống" : mode == "choice" ? "Trắc nghiệm ôn tập" : "Đánh giá kết hợp") + " · " + offering.CourseTitle, Description = "Đề giả lập phục vụ kiểm thử; câu trắc nghiệm tương ứng nguồn nhập DOCX, câu tự luận tương ứng trình soạn thủ công.", DurationMinutes = 60, AttemptsAllowed = 2, PassingScore = 5, Weight = i == 2 ? mode == "essay" ? 60 : 40 : 100, MultipleChoiceWeight = mode == "essay" ? 0 : mode == "choice" ? 100 : 60, EssayWeight = mode == "essay" ? 100 : mode == "choice" ? 0 : 40, OpensAt = opens, ClosesAt = closes, Status = status, Revision = 2, CreatedAt = opens.AddDays(-1) }, key);
                    var questions = new List<QuizQuestion>();
                    if (mode != "essay")
                    {
                        string[][] options = [["Xác định vấn đề và dữ kiện", "Kết luận ngay", "Bỏ qua tài liệu", "Sao chép ý kiến"], ["Ghi nguồn tài liệu", "Phân biệt dữ kiện và giả định", "Bỏ qua dữ kiện bất lợi", "Không kiểm tra trích dẫn"], ["Đúng", "Sai"]];
                        string[] contents = ["Khi đọc một tình huống học tập mới, việc nào nên làm trước?", "Chọn các bước cần có khi trình bày bài phân tích.", "Cần đối chiếu tài liệu được giao trước khi trình bày kết luận học tập."];
                        for (var q = 0; q < 3; q++)
                        {
                            var questionKey = key + ":question:" + q;
                            questions.Add(await Put(new QuizQuestion { Id = Id(questionKey), QuizId = quiz.Id, Content = contents[q], Type = (QuestionType)q, OptionsJson = JsonSerializer.Serialize(options[q]), CorrectAnswer = q == 1 ? JsonSerializer.Serialize(options[q].Take(2)) : options[q][0], Points = mode == "mixed" ? 2 : q == 0 ? 3 : q == 1 ? 4 : 3, Position = q + 1 }, questionKey));
                        }
                    }
                    if (mode != "choice")
                    {
                        for (var q = 0; q < (mode == "essay" ? 2 : 1); q++)
                        {
                            var questionKey = key + ":essay:" + q;
                            questions.Add(await Put(new QuizQuestion { Id = Id(questionKey), QuizId = quiz.Id, Content = q == 0 ? "Phân tích tình huống nhóm A và nhóm B trong bài học: xác định vấn đề, dữ kiện còn thiếu và lập luận cần làm rõ." : "Đề xuất dàn ý giải quyết tình huống, nêu tài liệu cần kiểm tra và giới hạn của kết luận.", Type = QuestionType.Essay, OptionsJson = "[]", CorrectAnswer = "", Points = mode == "mixed" ? 4 : 5, Position = questions.Count + 1 }, questionKey));
                        }
                    }
                    quizRows.Add((quiz, questions, i, key));
                    if (status != QuizStatus.Draft) await Log(key + ":submit", offering.PrimaryLecturerId, offering.Id, "quiz.submit", new { quizId = quiz.Id, reason = "Gửi đề kiểm thử" }, quiz.CreatedAt);
                    if (status is QuizStatus.Published or QuizStatus.Closed) await Log(key + ":publish", head.Id, offering.Id, "quiz.publish", new { quizId = quiz.Id, reason = "Duyệt đề kiểm thử" }, opens.AddMinutes(-10));
                    if (status == QuizStatus.Closed) await Log(key + ":close", offering.PrimaryLecturerId, offering.Id, "quiz.close", new { quizId = quiz.Id, reason = "Kết thúc đợt đánh giá" }, closes);
                }
            }

            foreach(var item in quizRows)
            {
                foreach(var question in item.Questions)
                {
                    var key=$"bank:{item.Quiz.Id}:{question.Id}";
                    var command=new CreateQuizQuestionCommand(question.Content,question.Type,JsonSerializer.Deserialize<List<string>>(question.OptionsJson)??[],question.CorrectAnswer,question.Points);
                    await Put(new BankQuestion {Id=Id(key),CourseId=item.Quiz.CourseId,CreatedById=item.Quiz.CreatedById,Content=question.Content,Type=question.Type,OptionsJson=question.OptionsJson,CorrectAnswer=question.CorrectAnswer,Points=question.Points,Topic="Tình huống kiểm thử",Difficulty=QuestionDifficulty.Medium,Source=BankQuestionSource.ExistingQuiz,Fingerprint=BankQuestionRules.Fingerprint(command),CreatedAt=now.AddDays(-5),UpdatedAt=now.AddDays(-5)},key);
                }
            }

            foreach (var (quiz, questions, offeringIndex, key) in quizRows.Where(x => x.Offering >= 2))
            {
                var offering = offerings[offeringIndex];
                for (var j = 0; j < 10; j++)
                {
                    if (offeringIndex == 2 && (j == 9 || quiz.Status == QuizStatus.Closed && j >= 7 || quiz.Status == QuizStatus.Published && j is 0 or 4 or 8)) continue;
                    if (offeringIndex == 3 && j is 0 or 8 or 9) continue;
                    var student = groups[groupIndices[offeringIndex]][j];
                    var keyAttempt = key + ":attempt:" + j;
                    var state = offeringIndex == 2 && quiz.Status == QuizStatus.Published && j == 1 || offeringIndex == 3 && j == 3 ? AttemptStatus.InProgress : offeringIndex == 2 && quiz.Status == QuizStatus.Published && j is 2 or 6 || offeringIndex == 3 && j == 1 ? AttemptStatus.Submitted : AttemptStatus.Graded;
                    var starts = state == AttemptStatus.InProgress ? now.AddMinutes(-5) : quiz.Status == QuizStatus.Closed ? quiz.ClosesAt!.Value.AddDays(-1).AddMinutes(j) : now.AddHours(-2).AddMinutes(j);
                    var fraction = new decimal[] { .9m, .7m, .6m, .8m, .4m, .3m, 1m, .75m, .65m, .5m }[j];
                    if (offeringIndex >= 5 && j == 5 && questions.Any(q => q.Type == QuestionType.Essay)) fraction = .1m;
                    var answerRows = new List<QuizAnswer>();
                    var attempt = new QuizAttempt { Id = Id(keyAttempt), QuizId = quiz.Id, StudentId = student.Id, StartedAt = starts, ExpiresAt = starts.AddMinutes(quiz.DurationMinutes), SubmittedAt = state == AttemptStatus.InProgress ? null : starts.AddMinutes(40), Status = state, Revision = state == AttemptStatus.Graded ? 3 : 1 };
                    foreach (var question in questions)
                    {
                        var answerKey = keyAttempt + ":answer:" + question.Id;
                        var essay = question.Type == QuestionType.Essay;
                        var answer = essay ? "Bài làm giả lập: Tôi xác định vấn đề, tách dữ kiện và giả định, liệt kê tài liệu được giao để đối chiếu, sau đó trình bày lập luận theo từng bước. Phần kết luận nêu rõ dữ kiện còn thiếu và câu hỏi cần thảo luận." : j % 3 == 0 || question.Position != 2 ? question.CorrectAnswer : "[]";
                        answerRows.Add(new QuizAnswer { Id = Id(answerKey), AttemptId = attempt.Id, QuestionId = question.Id, Answer = answer, Score = state == AttemptStatus.InProgress || essay && state != AttemptStatus.Graded ? null : essay ? decimal.Round(question.Points * fraction, 2) : AssessmentRules.Score(question.Type, answer, question.CorrectAnswer, question.Points), Feedback = state == AttemptStatus.Graded ? fraction < .5m ? "Cần bổ sung dữ kiện và làm rõ căn cứ trong lập luận." : "Đã xác định được vấn đề; tiếp tục làm rõ cách đối chiếu tài liệu." : "" });
                    }
                    attempt.AutoScore = answerRows.Where(a => questions.Any(q => q.Id == a.QuestionId && q.Type != QuestionType.Essay)).Sum(a => a.Score ?? 0);
                    attempt.FinalScore = state == AttemptStatus.Graded ? AssessmentRules.WeightedQuestionTotal(questions, answerRows, quiz.MultipleChoiceWeight, quiz.EssayWeight) : null;
                    await Put(attempt, keyAttempt);
                    foreach (var answer in answerRows) await Put(answer, answer.Id);
                    await Log(keyAttempt + ":start", student.Id, offering.Id, "attempt.start", new { id = attempt.Id, quizId = quiz.Id }, starts);
                    if (state != AttemptStatus.InProgress) await Log(keyAttempt + ":submit", student.Id, offering.Id, "attempt.submit", new { id = attempt.Id }, attempt.SubmittedAt!.Value);
                    if (state == AttemptStatus.Graded) await Log(keyAttempt + ":grade", offering.PrimaryLecturerId, offering.Id, "attempt.grade", new { id = attempt.Id, attempt.FinalScore }, attempt.SubmittedAt!.Value.AddHours(2));
                    if (offeringIndex == 5 && j < 4)
                    {
                        var appealKey = $"appeal:{j}";
                        var appealState = j == 0 ? AppealStatus.Pending : j == 1 ? AppealStatus.Reviewed : j == 2 ? AppealStatus.Resolved : AppealStatus.Rejected;
                        var proposal = answerRows.Select(a => new { a.QuestionId, score = a.Score ?? 0, feedback = "Đã đối soát lại theo thang điểm." }).ToList();
                        await Put(new GradeAppeal { Id = Id(appealKey), TeachingClassId = offering.Id, StudentId = student.Id, AttemptId = attempt.Id, Reason = "Đề nghị đối soát phần lập luận và cách phân bổ điểm trong bài kiểm thử.", Resolution = j == 0 ? "" : j == 1 ? "Đã chấm lại, chờ duyệt đề xuất." : j == 2 ? "Đã đối soát; giữ nguyên điểm sau kiểm tra." : "Đã trả lời yêu cầu đối soát.", ReviewerId = j == 0 ? null : offering.AssistantLecturerOneId, ProposedGradesJson = j is 1 or 2 ? JsonSerializer.Serialize(proposal, json) : "[]", Status = appealState, CreatedAt = now.AddDays(-1), Revision = j + 1 }, appealKey);
                        await Log(appealKey, j == 0 ? student.Id : j == 1 ? offering.AssistantLecturerOneId : head.Id, offering.Id, j == 0 ? "appeal.create" : j == 1 ? "appeal.propose" : j == 2 ? "appeal.approve" : "appeal.reject", new { attemptId = attempt.Id, seed = true }, now.AddHours(-12 + j));
                    }
                }
            }
            await db.SaveChangesAsync(ct);
            var allowedQuiz = quizRows.Single(x => x.Offering == 2 && x.Quiz.Status == QuizStatus.Published).Quiz;
            await Put(new QuizAllowance { Id = Id("allowance"), QuizId = allowedQuiz.Id, StudentId = groups[0][0].Id, ExtraAttempts = 1, ExtendedUntil = now.AddDays(16) }, "allowance");
            await Log("allowance", head.Id, offerings[2].Id, "quiz.allowance", new { quizId = allowedQuiz.Id, studentId = groups[0][0].Id, reason = "Gia hạn và thêm lượt phục vụ kiểm thử" }, now.AddHours(-1));

            for (var i = 2; i < offerings.Count; i++)
            {
                var offering = offerings[i];
                for (var topicIndex = 0; topicIndex < 2; topicIndex++)
                {
                    var key = $"topic:{i}:{topicIndex}";
                    var author = groups[groupIndices[i]][topicIndex];
                    var created = i >= 6 ? offering.EndsAt!.Value.AddDays(-10) : now.AddDays(-3);
                    var topic = await Put(new ForumTopic { Id = Id(key), TeachingClassId = offering.Id, AuthorId = author.Id, Title = topicIndex == 0 ? "Trao đổi cách phân tích tình huống" : "Thảo luận nội dung ôn tập", Body = "Em đã đọc bài học và liệt kê các dữ kiện. Nhờ giảng viên hướng dẫn cách sắp xếp lập luận và tài liệu cần đối chiếu cho tình huống của lớp.", CreatedAt = created, LastActivityAt = created.AddHours(3), Revision = 1 }, key);
                    for (var r = 0; r < 3; r++)
                    {
                        var replyKey = key + ":reply:" + r;
                        await Put(new ForumReply { Id = Id(replyKey), TopicId = topic.Id, AuthorId = r == 1 ? offering.PrimaryLecturerId : groups[groupIndices[i]][r + 2].Id, Body = r == 1 ? "Hãy tách phần dữ kiện, câu hỏi và giả định; đối chiếu tài liệu được giao, sau đó giải thích từng bước lập luận. Đây là hướng dẫn cho tình huống kiểm thử." : "Em đề xuất lập bảng dữ kiện và cùng trao đổi thêm những thông tin còn thiếu trước khi kết luận.", IsPinnedAnswer = r == 1 && topicIndex == 0, CreatedAt = created.AddHours(r + 1) }, replyKey);
                    }
                    foreach (var userId in new[] { author.Id, offering.PrimaryLecturerId, offering.AssistantLecturerOneId }) await Put(new ForumSubscription { TopicId = topic.Id, UserId = userId, SubscribedAt = created }, topic.Id, userId);
                }
                foreach (var (student, index) in groups[groupIndices[i]].Select((s, index) => (s, index)))
                {
                    if (i == 2 && index == 9) continue;
                    var key = $"notification:{i}:{student.Id}";
                    await Put(new Notification { Id = Id(key), UserId = student.Id, TeachingClassId = offering.Id, DedupKey = "test-seed:" + key, Message = i >= 5 ? "Kết quả kiểm thử đã công bố: " + offering.CourseTitle : "Theo dõi bài học và lịch kiểm tra của " + offering.CourseTitle, CreatedAt = i >= 6 ? now.AddDays(-30) : now.AddHours(-6), ReadAt = index % 3 == 0 ? now.AddHours(-1) : null }, key);
                }
            }
            await Put(new Notification { Id = Id("notification:head"), UserId = head.Id, TeachingClassId = offerings[4].Id, DedupKey = "test-seed:head:grade-review", Message = "Bảng điểm kiểm thử cần duyệt · TEST-K49A1 · Luật Hành chính", CreatedAt = now.AddHours(-2) }, "notification:head");
            await Put(new Notification { Id = Id("notification:teacher"), UserId = offerings[2].PrimaryLecturerId, TeachingClassId = offerings[2].Id, DedupKey = "test-seed:teacher:grading", Message = "Có bài tự luận kiểm thử đang chờ chấm.", CreatedAt = now.AddHours(-2) }, "notification:teacher");

            string[] categories = ["Bài giảng", "Văn bản pháp luật", "Đề cương", "Tài liệu tham khảo"];
            Directory.CreateDirectory(storageRoot);
            for (var i = 0; i < categories.Length; i++)
            {
                var key = $"resource:{i}";
                var storedName = "test-seed-" + Id(key).ToString("N") + ".zip";
                var path = Path.Combine(storageRoot, storedName);
                if (!File.Exists(path))
                {
                    await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    createdFiles.Add(path);
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
                    var entry = archive.CreateEntry("NOI-DUNG-KIEM-THU.md");
                    await using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                    await writer.WriteAsync($"# {categories[i]} – bộ kiểm thử LMS\n\nTệp mẫu giả lập để kiểm tra tải học liệu, tên tệp và danh mục.\nKhông phải giáo trình hay văn bản pháp luật chính thức.\n\n## Bài tập\nXác định vấn đề, liệt kê dữ kiện, tìm tài liệu được giao và trình bày lập luận.\n");
                }
                var bytes = new FileInfo(path).Length;
                await Put(new LibraryResource { Id = Id(key), Title = "[TEST] " + categories[i] + " – bài tập phương pháp phân tích", Description = "ZIP có nội dung Markdown đọc được, phục vụ kiểm tra tải xuống; không giả lập một PDF hoặc văn bản chính thức.", Category = categories[i], OriginalFileName = $"hoc-lieu-kiem-thu-{i + 1}.zip", StoredFileName = storedName, ContentType = "application/zip", SizeBytes = bytes, UploadedById = teachers[i].Id, UploadedAt = now.AddDays(-5 + i) }, key);
            }
            await Log("seed-created", admin.Id, Id("dataset"), "test.seed", new { dataset = "law-lms-qa-v1", note = "Explicit local fixtures; existing data is retained." }, now);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new { dataset = "law-lms-qa-v1", seededAt = now, added, retained, accounts = new { admin = admin.Email, head = head.Email, lecturer = teachers[2].Email, student = groups[0][0].Email }, classCodes = new[] { "TEST-K49A1", "TEST-K49A2", "TEST-K49A3" } };
        }
        catch
        {
            foreach (var path in createdFiles) File.Delete(path);
            throw;
        }
    }
}
