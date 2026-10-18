using System.Text.Json;
using System.Security.Cryptography;
using LawLms.Application;
using LawLms.Domain;
using LawLms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LawLms.Infrastructure.Services;

public sealed class OperationsService(LmsDbContext db, ITokenService tokens) : IOperationsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static void Require(bool condition, string message) { if (!condition) throw new ConflictException(message); }
    private static void Head(Actor a) { if (!a.IsHead) throw new UnauthorizedAppException("Chỉ Trưởng khoa hoặc Quản trị viên được xử lý."); }
    private static void Lecturer(Actor a) { if (a.Role != UserRole.Lecturer) throw new UnauthorizedAppException("Chỉ giảng viên được phân công mới được soạn và xử lý đề."); }
    private static void Admin(Actor a) { if (a.Role != UserRole.Admin) throw new UnauthorizedAppException("Chỉ Quản trị viên được xử lý tài khoản."); }
    private static void Version(int actual, int expected) => Require(actual == expected, "Dữ liệu đã thay đổi. Hãy tải lại trước khi xử lý.");
    private void Log(Actor actor, Guid id, string action, object detail) => db.AuditEntries.Add(new() { ActorId = actor.Id, EntityId = id, Action = action, Detail = JsonSerializer.Serialize(detail, JsonOptions) });
    private async Task<TeachingClass> Offering(Guid id, Actor actor, CancellationToken ct, bool staff = true)
    {
        var item = await db.TeachingClasses.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Không tìm thấy lớp học phần.");
        if (actor.IsHead || actor.Role == UserRole.Lecturer && (item.PrimaryLecturerId == actor.Id || item.AssistantLecturerOneId == actor.Id || item.AssistantLecturerTwoId == actor.Id)) return item;
        if (!staff && actor.Role == UserRole.Student && item.Status >= OfferingStatus.Active && await db.OfferingEnrollments.AnyAsync(x => x.TeachingClassId == id && x.UserId == actor.Id && x.Status != EnrollmentStatus.Withdrawn, ct)) return item;
        throw new UnauthorizedAppException("Bạn không được truy cập lớp học phần này.");
    }
    private async Task Lock(Guid id, CancellationToken ct) => await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({id.ToString()}))", ct);
    private async Task<TeachingClass> LockedOffering(Guid id, Actor actor, CancellationToken ct)
    {
        await Lock(id, ct);
        var item = await Offering(id, actor, ct);
        await db.Entry(item).ReloadAsync(ct);
        return item;
    }
    private static void Mutable(TeachingClass item) => Require(item.Status < OfferingStatus.Completed, "Lớp đã kết thúc và chỉ được đọc.");
    private static void Reason(string reason) => Require(!string.IsNullOrWhiteSpace(reason) && reason.Length <= 2000, "Cần lý do từ 1 đến 2000 ký tự.");

    public async Task<IReadOnlyList<OfferingView>> OfferingsAsync(Actor actor, CancellationToken ct)
    {
        var query = db.TeachingClasses.AsNoTracking().AsQueryable();
        if (actor.Role == UserRole.Lecturer) query = query.Where(x => x.PrimaryLecturerId == actor.Id || x.AssistantLecturerOneId == actor.Id || x.AssistantLecturerTwoId == actor.Id);
        if (actor.Role == UserRole.Student) query = query.Where(x => x.Status >= OfferingStatus.Active && db.OfferingEnrollments.Any(e => e.TeachingClassId == x.Id && e.UserId == actor.Id && e.Status != EnrollmentStatus.Withdrawn));
        var items = await query.OrderByDescending(x => x.AssignedAt).ToListAsync(ct);
        var result = new List<OfferingView>();
        foreach (var x in items)
        {
            var enrollment = actor.Role == UserRole.Student ? await db.OfferingEnrollments.AsNoTracking().SingleOrDefaultAsync(e => e.TeachingClassId == x.Id && e.UserId == actor.Id, ct) : null;
            var completed = enrollment is null ? [] : await db.LessonCompletions.Where(c => c.EnrollmentId == enrollment.Id).Select(c => c.LessonId).ToListAsync(ct);
            var count = await db.Lessons.CountAsync(l => l.CourseId == x.CourseId, ct);
            result.Add(new(x.Id,x.CourseId,x.Code,x.Name,x.CourseTitle,x.AcademicYear,x.Term,x.Status,x.StartsAt,x.EndsAt,x.PassingGrade,x.RequiredProgress,x.AppealDays,x.GradeStatus,x.GradesPublishedAt,x.Revision,await db.OfferingEnrollments.CountAsync(e => e.TeachingClassId == x.Id && e.Status != EnrollmentStatus.Withdrawn, ct),count == 0 ? 0 : completed.Count * 100 / count,completed,actor.Role==UserRole.Lecturer && x.PrimaryLecturerId==actor.Id));
        }
        return result;
    }
    public async Task ConfigureAsync(Guid id, OfferingSettings settings, Actor actor, CancellationToken ct)
    {
        settings = settings with { StartsAt = settings.StartsAt.ToUniversalTime(), EndsAt = settings.EndsAt.ToUniversalTime() };
        Head(actor); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var item = await LockedOffering(id,actor,ct); Version(item.Revision,settings.Revision);
        Require(item.Status == OfferingStatus.Draft,"Chỉ cấu hình lớp ở trạng thái nháp.");
        Require(!string.IsNullOrWhiteSpace(settings.Term) && settings.Term.Length <= 30 && settings.StartsAt < settings.EndsAt && settings.PassingGrade is >= 0 and <= 10 && settings.RequiredProgress is >= 0 and <= 100 && settings.AppealDays is >= 1 and <= 30,"Lịch học hoặc chính sách kết quả không hợp lệ.");
        item.Term=settings.Term.Trim(); item.StartsAt=settings.StartsAt; item.EndsAt=settings.EndsAt; item.PassingGrade=settings.PassingGrade; item.RequiredProgress=settings.RequiredProgress; item.AppealDays=settings.AppealDays; item.Revision++;
        Log(actor,id,"offering.configure",settings); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task TransitionAsync(Guid id, WorkflowAction command, Actor actor, CancellationToken ct)
    {
        Head(actor); Reason(command.Reason); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var item=await LockedOffering(id,actor,ct); Version(item.Revision,command.Revision);
        switch(command.Action)
        {
            case "ready":
                Require(item.Status==OfferingStatus.Draft,"Lớp chưa ở trạng thái nháp.");
                Require(item.StartsAt.HasValue && item.EndsAt > item.StartsAt,"Cần cấu hình lịch và chính sách lớp.");
                Require(await db.Courses.AnyAsync(x=>x.Id==item.CourseId && x.IsPublished,ct),"Học phần phải được duyệt và công bố.");
                Require(await db.OfferingEnrollments.AnyAsync(x=>x.TeachingClassId==id && x.Status==EnrollmentStatus.Active,ct),"Lớp chưa có học viên.");
                Require(await db.Lessons.AnyAsync(x=>x.CourseId==item.CourseId,ct),"Học phần chưa có bài học.");
                item.Status=OfferingStatus.Ready; break;
            case "activate":
                Require(item.Status==OfferingStatus.Ready && item.StartsAt<=DateTime.UtcNow && item.EndsAt>DateTime.UtcNow,"Lớp phải sẵn sàng và nằm trong lịch học.");
                item.Status=OfferingStatus.Active; break;
            case "grading":
                Require(item.Status==OfferingStatus.Active,"Lớp chưa hoạt động.");
                Require(!await db.QuizAttempts.AnyAsync(a=>a.Status==AttemptStatus.InProgress && db.Quizzes.Any(q=>q.Id==a.QuizId && q.TeachingClassId==id),ct),"Còn lượt đang làm bài; hãy đóng kiểm tra và chờ xử lý hết hạn.");
                item.Status=OfferingStatus.Grading; break;
            case "complete":
                Require(item.Status==OfferingStatus.Grading && item.GradeStatus==GradebookStatus.Published,"Phải chấm, duyệt và công bố điểm trước khi kết thúc.");
                Require(item.GradesPublishedAt?.AddDays(item.AppealDays)<=DateTime.UtcNow,"Chưa hết thời hạn phúc khảo.");
                Require(!await db.GradeAppeals.AnyAsync(x=>x.TeachingClassId==id && (x.Status==AppealStatus.Pending || x.Status==(AppealStatus)3),ct),"Còn yêu cầu phúc khảo chưa xử lý.");
                item.Status=OfferingStatus.Completed; item.GradeStatus=GradebookStatus.Locked;
                foreach(var e in await db.OfferingEnrollments.Where(x=>x.TeachingClassId==id && x.Status==EnrollmentStatus.Active).ToListAsync(ct)) e.Status=EnrollmentStatus.Completed;
                break;
            case "archive": Require(item.Status==OfferingStatus.Completed,"Chỉ lưu trữ lớp đã kết thúc."); item.Status=OfferingStatus.Archived; break;
            default: throw new ConflictException("Thao tác chuyển trạng thái không hợp lệ.");
        }
        item.Revision++; Log(actor,id,"offering."+command.Action,command); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task<IReadOnlyList<RosterRow>> RosterAsync(Guid id,Actor actor,CancellationToken ct)
    {
        var item=await Offering(id,actor,ct); var lessons=await db.Lessons.CountAsync(x=>x.CourseId==item.CourseId,ct);
        return await (from e in db.OfferingEnrollments.AsNoTracking() join u in db.Users on e.UserId equals u.Id where e.TeachingClassId==id orderby u.FullName select new RosterRow(e.Id,u.Id,u.FullName,u.Email,e.Status,lessons==0?0:db.LessonCompletions.Count(c=>c.EnrollmentId==e.Id)*100/lessons)).ToListAsync(ct);
    }
    public async Task SyncRosterAsync(Guid id,string reason,Actor actor,CancellationToken ct)
    {
        Head(actor); Reason(reason); await using var tx=await db.Database.BeginTransactionAsync(ct); var item=await LockedOffering(id,actor,ct); Mutable(item);
        Require(item.GradeStatus==GradebookStatus.Draft,"Cần mở lại bảng điểm trước khi đổi danh sách.");
        var students=await db.Users.Where(x=>x.Role==UserRole.Student && x.IsActive && x.ClassCode==item.Code).ToListAsync(ct);
        var existing=await db.OfferingEnrollments.Where(x=>x.TeachingClassId==id).ToListAsync(ct); var ids=students.Select(x=>x.Id).ToHashSet();
        foreach(var e in existing) e.Status=ids.Contains(e.UserId)?EnrollmentStatus.Active:EnrollmentStatus.Withdrawn;
        foreach(var student in students.Where(x=>existing.All(e=>e.UserId!=x.Id))) db.OfferingEnrollments.Add(new(){TeachingClassId=id,UserId=student.Id});
        item.Revision++; Log(actor,id,"roster.sync",new {reason,students=students.Count}); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task ChangeEnrollmentAsync(Guid id,EnrollmentChange command,Actor actor,CancellationToken ct)
    {
        Head(actor); Reason(command.Reason); await using var tx=await db.Database.BeginTransactionAsync(ct); var item=await LockedOffering(id,actor,ct); Mutable(item);
        Require(item.GradeStatus==GradebookStatus.Draft,"Cần mở lại bảng điểm trước khi đổi danh sách.");
        Require(await db.Users.AnyAsync(x=>x.Id==command.UserId && x.Role==UserRole.Student && x.IsActive,ct),"Học viên không hợp lệ.");
        var enrollment=await db.OfferingEnrollments.SingleOrDefaultAsync(x=>x.TeachingClassId==id && x.UserId==command.UserId,ct);
        if(enrollment is null){enrollment=new(){TeachingClassId=id,UserId=command.UserId};db.OfferingEnrollments.Add(enrollment);}
        enrollment.Status=command.Active?EnrollmentStatus.Active:EnrollmentStatus.Withdrawn;
        item.Revision++; Log(actor,id,"roster.change",command); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task CompleteLessonAsync(Guid id,Guid lessonId,Actor actor,CancellationToken ct)
    {
        Require(actor.Role==UserRole.Student,"Chỉ học viên ghi nhận hoàn thành bài.");
        await using var tx=await db.Database.BeginTransactionAsync(ct); await Lock(id,ct); var item=await Offering(id,actor,ct,false);
        Require(item.Status==OfferingStatus.Active && item.EndsAt>=DateTime.UtcNow && item.GradeStatus==GradebookStatus.Draft,"Chỉ ghi nhận tiến độ khi lớp đang học và bảng điểm chưa chốt.");
        Require(await db.Lessons.AnyAsync(x=>x.Id==lessonId && x.CourseId==item.CourseId,ct),"Bài học không thuộc học phần.");
        var enrollment=await db.OfferingEnrollments.SingleAsync(x=>x.TeachingClassId==id && x.UserId==actor.Id && x.Status==EnrollmentStatus.Active,ct);
        if(!await db.LessonCompletions.AnyAsync(x=>x.EnrollmentId==enrollment.Id && x.LessonId==lessonId,ct))
        {db.LessonCompletions.Add(new(){EnrollmentId=enrollment.Id,LessonId=lessonId});Log(actor,id,"lesson.complete",new{lessonId});await db.SaveChangesAsync(ct);}
        await tx.CommitAsync(ct);
    }
    public async Task<IReadOnlyList<ManagedQuiz>> QuizzesAsync(Guid id,Actor actor,CancellationToken ct)
    {
        var item=await Offering(id,actor,ct,false);
        var query=db.Quizzes.AsNoTracking().Where(x=>x.TeachingClassId==id);
        if(actor.Role==UserRole.Student) query=query.Where(x=>x.Status==QuizStatus.Published || x.Status==QuizStatus.Closed);
        return await query.OrderBy(x=>x.OpensAt).Select(x=>new ManagedQuiz(x.Id,id,x.Title,x.Status,x.Weight,x.MultipleChoiceWeight,x.EssayWeight,x.Revision,x.OpensAt,x.ClosesAt,x.Questions.Count,x.ShuffleQuestions,x.ShuffleOptions)).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<QuizReviewQuestion>> PreviewQuizAsync(Guid id,Actor actor,CancellationToken ct)
    {
        var quiz=await Quiz(id,ct);await Offering(quiz.TeachingClassId!.Value,actor,ct);
        return quiz.Questions.OrderBy(x=>x.Position).Select(x=>new QuizReviewQuestion(x.Content,x.Type,x.Points,JsonSerializer.Deserialize<List<string>>(x.OptionsJson)??[],x.CorrectAnswer)).ToList();
    }
    private static void ValidateQuiz(QuizSetup c,TeachingClass item)
    {
        Require(!string.IsNullOrWhiteSpace(c.Title) && c.Title.Length<=300 && c.Description.Length<=4000 && c.DurationMinutes is >=1 and <=600 && c.AttemptsAllowed is >=1 and <=10 && c.Weight is >0 and <=100 && c.MultipleChoiceWeight>=0 && c.MultipleChoiceWeight<=100 && c.EssayWeight>=0 && c.EssayWeight<=100 && c.MultipleChoiceWeight+c.EssayWeight==100,"Trọng số trắc nghiệm và tự luận phải nằm trong 0–100% và cộng đúng 100%.");
        Require(c.OpensAt<c.ClosesAt && c.OpensAt>=item.StartsAt && c.ClosesAt<=item.EndsAt,"Lịch kiểm tra phải nằm trong lịch lớp học phần.");
        Require(c.Questions.Count is >=1 and <=200 && c.Questions.All(x=>!string.IsNullOrWhiteSpace(x.Content) && x.Content.Length<=5000 && x.Points>0 && x.Points<=100000 && Enum.IsDefined(x.Type)),"Câu hỏi không hợp lệ.");
        Require(c.PassingScore>=0 && c.PassingScore<=c.Questions.Sum(x=>x.Points),"Điểm đạt vượt quá tổng điểm đề.");
        foreach(var q in c.Questions.Where(x=>x.Type!=QuestionType.Essay))
        {
            Require(q.Options.Count>=2 && q.Options.Distinct().Count()==q.Options.Count,"Phương án phải khác nhau và có ít nhất hai lựa chọn.");
            if(q.Type==QuestionType.MultipleChoice){try{var keys=JsonSerializer.Deserialize<string[]>(q.CorrectAnswer)??[];Require(keys.Length>0&&keys.All(q.Options.Contains),"Đáp án nhiều lựa chọn không hợp lệ.");}catch(JsonException){throw new ConflictException("Đáp án nhiều lựa chọn phải là mảng JSON.");}}
            else Require(q.Options.Contains(q.CorrectAnswer),"Đáp án đúng phải thuộc danh sách phương án.");
        }
    }
    public async Task<QuizDraft> GetQuizDraftAsync(Guid id,Actor actor,CancellationToken ct)
    {
        Lecturer(actor);var quiz=await Quiz(id,ct);await Offering(quiz.TeachingClassId!.Value,actor,ct);
        Require(quiz.Status==QuizStatus.Draft,"Chỉ chỉnh sửa đề ở trạng thái nháp.");
        return new(new(quiz.TeachingClassId.Value,quiz.Title,quiz.Description,quiz.DurationMinutes,quiz.AttemptsAllowed,quiz.PassingScore,quiz.OpensAt??DateTime.UtcNow,quiz.ClosesAt??DateTime.UtcNow,quiz.Weight,quiz.Questions.OrderBy(x=>x.Position).Select(x=>new CreateQuizQuestionCommand(x.Content,x.Type,JsonSerializer.Deserialize<List<string>>(x.OptionsJson)??[],x.CorrectAnswer,x.Points)).ToList(),quiz.MultipleChoiceWeight,quiz.EssayWeight,quiz.ShuffleQuestions,quiz.ShuffleOptions),quiz.Revision);
    }
    public async Task ReviseQuizAsync(Guid id,QuizDraft command,Actor actor,CancellationToken ct)
    {
        Lecturer(actor);var quiz=await Quiz(id,ct);await using var tx=await db.Database.BeginTransactionAsync(ct);var item=await LockedOffering(quiz.TeachingClassId!.Value,actor,ct);await db.Entry(quiz).ReloadAsync(ct);Mutable(item);Version(quiz.Revision,command.Revision);
        Require(quiz.Status==QuizStatus.Draft && item.GradeStatus==GradebookStatus.Draft && item.Status<=OfferingStatus.Active,"Chỉ sửa bản nháp trước khi chốt đánh giá.");
        Require(command.Setup.TeachingClassId==item.Id && !await db.QuizAttempts.AnyAsync(x=>x.QuizId==id,ct),"Không được đổi lớp hoặc sửa đề đã có lượt làm.");
        var c=command.Setup with{OpensAt=command.Setup.OpensAt.ToUniversalTime(),ClosesAt=command.Setup.ClosesAt.ToUniversalTime()};ValidateQuiz(c,item);
        quiz.ShuffleQuestions=c.ShuffleQuestions;quiz.ShuffleOptions=c.ShuffleOptions;
        db.QuizQuestions.RemoveRange(quiz.Questions);quiz.Questions.Clear();
        foreach(var row in c.Questions.Select((x,i)=>new QuizQuestion{QuizId=id,Content=x.Content,Type=x.Type,OptionsJson=JsonSerializer.Serialize(x.Options),CorrectAnswer=x.CorrectAnswer,Points=x.Points,Position=i+1})){db.QuizQuestions.Add(row);quiz.Questions.Add(row);}
        quiz.Title=c.Title.Trim();quiz.Description=c.Description;quiz.DurationMinutes=c.DurationMinutes;quiz.AttemptsAllowed=c.AttemptsAllowed;quiz.PassingScore=c.PassingScore;quiz.OpensAt=c.OpensAt;quiz.ClosesAt=c.ClosesAt;quiz.Weight=c.Weight;quiz.MultipleChoiceWeight=c.MultipleChoiceWeight;quiz.EssayWeight=c.EssayWeight;quiz.Revision++;item.Revision++;
        Log(actor,item.Id,"quiz.revise",new{id,quiz.Revision,quiz.Title,quiz.MultipleChoiceWeight,quiz.EssayWeight,quiz.ShuffleQuestions,quiz.ShuffleOptions});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task RemoveQuizDraftAsync(Guid id,WorkflowAction command,Actor actor,CancellationToken ct)
    {
        Lecturer(actor);Reason(command.Reason);var quiz=await Quiz(id,ct);await using var tx=await db.Database.BeginTransactionAsync(ct);var item=await LockedOffering(quiz.TeachingClassId!.Value,actor,ct);await db.Entry(quiz).ReloadAsync(ct);Mutable(item);Version(quiz.Revision,command.Revision);
        Require(quiz.Status==QuizStatus.Draft && item.GradeStatus==GradebookStatus.Draft && !await db.QuizAttempts.AnyAsync(x=>x.QuizId==id,ct),"Chỉ xóa đề nháp chưa có bài làm.");
        db.Quizzes.Remove(quiz);item.Revision++;Log(actor,item.Id,"quiz.remove-draft",new{id,quiz.Title,command.Reason});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task<Guid> CreateQuizAsync(QuizSetup c,Actor actor,CancellationToken ct)
    {
        Lecturer(actor);
        c = c with { OpensAt = c.OpensAt.ToUniversalTime(), ClosesAt = c.ClosesAt.ToUniversalTime() };
        await using var tx=await db.Database.BeginTransactionAsync(ct);var item=await LockedOffering(c.TeachingClassId,actor,ct);Mutable(item);
        Require(item.Status is OfferingStatus.Draft or OfferingStatus.Ready or OfferingStatus.Active,"Không thể thêm đề khi lớp đã chấm điểm.");
        Require(item.GradeStatus==GradebookStatus.Draft,"Bảng điểm đã được gửi duyệt.");
        ValidateQuiz(c,item);
        var quiz=new Quiz{TeachingClassId=item.Id,CourseId=item.CourseId,ClassCode=item.Code,CreatedById=actor.Id,Title=c.Title.Trim(),Description=c.Description,DurationMinutes=c.DurationMinutes,AttemptsAllowed=c.AttemptsAllowed,PassingScore=c.PassingScore,OpensAt=c.OpensAt,ClosesAt=c.ClosesAt,Weight=c.Weight,MultipleChoiceWeight=c.MultipleChoiceWeight,EssayWeight=c.EssayWeight};
        quiz.ShuffleQuestions=c.ShuffleQuestions;quiz.ShuffleOptions=c.ShuffleOptions;
        quiz.Questions=c.Questions.Select((x,i)=>new QuizQuestion{Content=x.Content,Type=x.Type,OptionsJson=JsonSerializer.Serialize(x.Options),CorrectAnswer=x.CorrectAnswer,Points=x.Points,Position=i+1}).ToList();
        db.Quizzes.Add(quiz); item.Revision++;Log(actor,item.Id,"quiz.create",new{quiz.Id,quiz.Title,quiz.Weight,quiz.MultipleChoiceWeight,quiz.EssayWeight,quiz.ShuffleQuestions,quiz.ShuffleOptions});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return quiz.Id;
    }
    public async Task ReviewQuizAsync(Guid id,WorkflowAction command,Actor actor,CancellationToken ct)
    {
        var quiz=await db.Quizzes.Include(x=>x.Questions).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new NotFoundException("Không tìm thấy đề.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);var item=await LockedOffering(quiz.TeachingClassId!.Value,actor,ct);await db.Entry(quiz).ReloadAsync(ct);Mutable(item); Version(quiz.Revision,command.Revision);
        Require(item.GradeStatus==GradebookStatus.Draft,"Bảng điểm đã được gửi duyệt.");
        switch(command.Action)
        {
            case "submit":Lecturer(actor);Require(quiz.Status==QuizStatus.Draft && quiz.Questions.Count>0,"Đề chưa sẵn sàng gửi duyệt.");quiz.Status=QuizStatus.PendingReview;break;
            case "publish":Head(actor);Require(quiz.Status==QuizStatus.PendingReview,"Đề chưa gửi duyệt.");Require(quiz.CreatedById!=actor.Id,"Người soạn đề không được tự duyệt.");Require(item.Status<=OfferingStatus.Active,"Lớp đã chuyển sang chấm điểm.");quiz.Status=QuizStatus.Published;break;
            case "reject":Head(actor);Reason(command.Reason);Require(quiz.Status==QuizStatus.PendingReview,"Đề chưa gửi duyệt.");quiz.Status=QuizStatus.Draft;break;
            case "close":Lecturer(actor);Require(quiz.Status==QuizStatus.Published,"Đề chưa được công bố.");Reason(command.Reason);quiz.Status=QuizStatus.Closed;quiz.ClosesAt=DateTime.UtcNow;break;
            default:throw new ConflictException("Thao tác không hợp lệ.");
        }
        quiz.Revision++;item.Revision++;Log(actor,item.Id,"quiz."+command.Action,new{id,command.Reason});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    private async Task<Quiz> Quiz(Guid id,CancellationToken ct) => await db.Quizzes.Include(x=>x.Questions).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new NotFoundException("Không tìm thấy bài kiểm tra.");
    private async Task<QuizAttempt> Attempt(Guid id,CancellationToken ct) => await db.QuizAttempts.Include(x=>x.Answers).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new NotFoundException("Không tìm thấy lượt làm bài.");
    private static AttemptView View(Quiz quiz,QuizAttempt attempt) => new(attempt.Id,quiz.Id,attempt.StartedAt,attempt.ExpiresAt,DateTime.UtcNow,attempt.SubmittedAt,attempt.Status,attempt.Revision,QuizPresentation.Questions(quiz,attempt),attempt.Answers.Select(x=>new SubmitQuizAnswerCommand(x.QuestionId,x.Answer)).ToList());
    private void FinalizeAttempt(Quiz quiz,QuizAttempt attempt,DateTime submitted)
    {
        if(attempt.Status!=AttemptStatus.InProgress)return;
        foreach(var question in quiz.Questions)
        {
            var answer=attempt.Answers.SingleOrDefault(x=>x.QuestionId==question.Id);
            if(answer is null){answer=new(){AttemptId=attempt.Id,QuestionId=question.Id};attempt.Answers.Add(answer);db.QuizAnswers.Add(answer);}
            answer.Score=question.Type==QuestionType.Essay?null:AssessmentRules.Score(question.Type,answer.Answer,question.CorrectAnswer,question.Points);
        }
        attempt.AutoScore=attempt.Answers.Where(a=>quiz.Questions.Any(q=>q.Id==a.QuestionId&&q.Type!=QuestionType.Essay)).Sum(x=>x.Score??0);attempt.FinalScore=quiz.Questions.Any(x=>x.Type==QuestionType.Essay)?null:AssessmentRules.WeightedQuestionTotal(quiz.Questions,attempt.Answers,quiz.MultipleChoiceWeight,quiz.EssayWeight);
        attempt.Status=attempt.FinalScore.HasValue?AttemptStatus.Graded:AttemptStatus.Submitted;attempt.SubmittedAt=submitted;attempt.Revision++;
    }
    public async Task<AttemptView> StartAsync(Guid quizId,Actor actor,CancellationToken ct)
    {
        if(actor.Role!=UserRole.Student)throw new UnauthorizedAppException("Chỉ học viên được bắt đầu bài kiểm tra.");
        var quiz=await Quiz(quizId,ct);var offeringId=quiz.TeachingClassId??throw new ConflictException("Đề chưa gắn lớp học phần.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(offeringId,ct);await Lock(actor.Id,ct);await db.Entry(quiz).ReloadAsync(ct);
        var item=await Offering(offeringId,actor,ct,false);
        var existing=await db.QuizAttempts.Include(x=>x.Answers).SingleOrDefaultAsync(x=>x.QuizId==quizId && x.StudentId==actor.Id && x.Status==AttemptStatus.InProgress,ct);
        if(existing is not null)
        {
            if(existing.ExpiresAt<=DateTime.UtcNow || quiz.Status==QuizStatus.Closed){FinalizeAttempt(quiz,existing,DateTime.UtcNow);Log(actor,item.Id,"attempt.expire",new{existing.Id});await db.SaveChangesAsync(ct);}
            await tx.CommitAsync(ct);return View(quiz,existing);
        }
        Require(item.Status==OfferingStatus.Active && item.GradeStatus==GradebookStatus.Draft,"Lớp chưa mở hoặc đã chốt đánh giá.");
        var now=DateTime.UtcNow;
        var allowance=await db.QuizAllowances.AsNoTracking().SingleOrDefaultAsync(x=>x.QuizId==quizId && x.StudentId==actor.Id,ct);
        var closes=allowance?.ExtendedUntil??quiz.ClosesAt;
        Require(quiz.Status==QuizStatus.Published && quiz.OpensAt<=now && closes>now,"Bài kiểm tra chưa mở hoặc đã đóng.");
        Require(await db.QuizAttempts.CountAsync(x=>x.QuizId==quizId && x.StudentId==actor.Id,ct)<quiz.AttemptsAllowed+(allowance?.ExtraAttempts??0),"Bạn đã dùng hết số lượt làm bài.");
        var attempt=new QuizAttempt{QuizId=quizId,StudentId=actor.Id,StartedAt=now,ExpiresAt=AssessmentRules.Deadline(now,quiz.DurationMinutes,closes!.Value),SubmittedAt=null};
        QuizPresentation.Initialize(quiz,attempt);
        db.QuizAttempts.Add(attempt);Log(actor,item.Id,"attempt.start",new{attempt.Id,attempt.ExpiresAt});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return View(quiz,attempt);
    }
    public async Task<AttemptView> SaveAsync(Guid attemptId,SaveAttempt command,bool submit,Actor actor,CancellationToken ct)
    {
        var attempt=await Attempt(attemptId,ct);
        if(actor.Role!=UserRole.Student || attempt.StudentId!=actor.Id)throw new UnauthorizedAppException("Lượt làm bài không thuộc về bạn.");
        var quiz=await Quiz(attempt.QuizId,ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(quiz.TeachingClassId!.Value,ct);await Lock(actor.Id,ct);await db.Entry(attempt).ReloadAsync(ct);await db.Entry(attempt).Collection(x=>x.Answers).LoadAsync(ct);await db.Entry(quiz).ReloadAsync(ct);
        if(attempt.Status!=AttemptStatus.InProgress){await tx.CommitAsync(ct);return View(quiz,attempt);}
        var item=await Offering(quiz.TeachingClassId.Value,actor,ct,false);
        if(attempt.ExpiresAt<=DateTime.UtcNow || quiz.Status==QuizStatus.Closed)
        {FinalizeAttempt(quiz,attempt,DateTime.UtcNow);Log(actor,item.Id,"attempt.expire",new{attempt.Id});}
        else
        {
            Require(item.Status==OfferingStatus.Active,"Lớp không còn nhận bài.");Version(attempt.Revision,command.Revision);
            Require(command.Answers.Count<=quiz.Questions.Count && command.Answers.Select(x=>x.QuestionId).Distinct().Count()==command.Answers.Count,"Danh sách câu trả lời bị trùng hoặc không hợp lệ.");
            foreach(var row in command.Answers)
            {
                Require(quiz.Questions.Any(x=>x.Id==row.QuestionId) && row.Answer.Length<=20000,"Câu trả lời không hợp lệ.");
                var answer=attempt.Answers.SingleOrDefault(x=>x.QuestionId==row.QuestionId);
                if(answer is null){answer=new(){AttemptId=attempt.Id,QuestionId=row.QuestionId};attempt.Answers.Add(answer);db.QuizAnswers.Add(answer);}answer.Answer=row.Answer;
            }
            attempt.Revision++;if(submit){FinalizeAttempt(quiz,attempt,DateTime.UtcNow);Log(actor,item.Id,"attempt.submit",new{attempt.Id});}
        }
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return View(quiz,attempt);
    }
    public async Task ExpireAttemptsAsync(CancellationToken ct)
    {
        var ids=await db.QuizAttempts.AsNoTracking().Where(x=>x.Status==AttemptStatus.InProgress && (x.ExpiresAt<=DateTime.UtcNow || db.Quizzes.Any(q=>q.Id==x.QuizId && q.Status==QuizStatus.Closed))).OrderBy(x=>x.ExpiresAt).Select(x=>x.Id).Take(100).ToListAsync(ct);
        foreach(var id in ids)
        {
            var attempt=await Attempt(id,ct);var quiz=await Quiz(attempt.QuizId,ct);
            await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(quiz.TeachingClassId!.Value,ct);await Lock(attempt.StudentId,ct);await db.Entry(attempt).ReloadAsync(ct);await db.Entry(attempt).Collection(x=>x.Answers).LoadAsync(ct);
            if(attempt.Status==AttemptStatus.InProgress && (attempt.ExpiresAt<=DateTime.UtcNow || quiz.Status==QuizStatus.Closed))
            {FinalizeAttempt(quiz,attempt,DateTime.UtcNow);Log(new(Guid.Empty,UserRole.Admin),quiz.TeachingClassId.Value,"attempt.expire",new{id});await db.SaveChangesAsync(ct);}
            await tx.CommitAsync(ct);
        }
    }
    public async Task<IReadOnlyList<AttemptForGrading>> GradingAsync(Guid id,Actor actor,CancellationToken ct)
    {
        await Offering(id,actor,ct);var quizzes=await db.Quizzes.AsNoTracking().Include(x=>x.Questions).Where(x=>x.TeachingClassId==id).ToDictionaryAsync(x=>x.Id,ct);
        var attempts=await db.QuizAttempts.AsNoTracking().Include(x=>x.Answers).Where(x=>quizzes.Keys.Contains(x.QuizId) && x.Status!=AttemptStatus.InProgress).OrderBy(x=>x.SubmittedAt).ToListAsync(ct);
        var users=await db.Users.Where(x=>attempts.Select(a=>a.StudentId).Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.FullName,ct);
        return attempts.Select(x=>new AttemptForGrading(x.Id,x.QuizId,x.StudentId,users[x.StudentId],quizzes[x.QuizId].Title,x.Status,x.Revision,x.FinalScore,GradingAnswers(quizzes[x.QuizId],x))).ToList();
    }
    public async Task<AttemptForGrading> ResultAsync(Guid id,Actor actor,CancellationToken ct)
    {
        var attempt=await Attempt(id,ct);var quiz=await Quiz(attempt.QuizId,ct);var item=await Offering(quiz.TeachingClassId!.Value,actor,ct,false);
        if(actor.Role==UserRole.Student && (attempt.StudentId!=actor.Id || item.GradeStatus is not (GradebookStatus.Published or GradebookStatus.Locked)))throw new UnauthorizedAppException("Kết quả chưa công bố hoặc không thuộc về bạn.");
        var name=await db.Users.Where(x=>x.Id==attempt.StudentId).Select(x=>x.FullName).SingleAsync(ct);
        return new(attempt.Id,quiz.Id,attempt.StudentId,name,quiz.Title,attempt.Status,attempt.Revision,attempt.FinalScore,GradingAnswers(quiz,attempt,actor.Role==UserRole.Student));
    }
    private static IReadOnlyList<AnswerForGrading> GradingAnswers(Quiz quiz,QuizAttempt attempt,bool hideGuide=false) => QuizPresentation.Questions(quiz,attempt).Select(q=>{
        var answer=attempt.Answers.SingleOrDefault(a=>a.QuestionId==q.Id);
        return new AnswerForGrading(q.Id,q.Content,q.Type,q.Points,answer?.Answer??"",answer?.Score,answer?.Feedback??"",hideGuide?"":quiz.Questions.Single(x=>x.Id==q.Id).CorrectAnswer);
    }).ToList();
    private static void ApplyGrades(Quiz quiz,QuizAttempt attempt,IReadOnlyList<GradeAnswer> answers)
    {
        Require(answers.Count==quiz.Questions.Count && answers.Select(x=>x.QuestionId).Distinct().Count()==answers.Count,"Phải chấm đầy đủ từng câu, không được trùng câu.");
        foreach(var row in answers)
        {
            var q=quiz.Questions.SingleOrDefault(x=>x.Id==row.QuestionId);Require(q is not null && row.Score>=0 && row.Score<=q.Points && row.Feedback.Length<=4000,"Điểm hoặc phản hồi không hợp lệ.");
            var answer=attempt.Answers.Single(x=>x.QuestionId==row.QuestionId);answer.Score=row.Score;answer.Feedback=row.Feedback;
        }
        attempt.FinalScore=AssessmentRules.WeightedQuestionTotal(quiz.Questions,attempt.Answers,quiz.MultipleChoiceWeight,quiz.EssayWeight);attempt.Status=AttemptStatus.Graded;attempt.Revision++;
    }
    public async Task GradeAsync(Guid id,GradeAttempt command,Actor actor,CancellationToken ct)
    {
        var attempt=await Attempt(id,ct);var quiz=await Quiz(attempt.QuizId,ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);var item=await LockedOffering(quiz.TeachingClassId!.Value,actor,ct);await db.Entry(attempt).ReloadAsync(ct);Mutable(item);Version(attempt.Revision,command.Revision);
        Require(item.GradeStatus==GradebookStatus.Draft,"Bảng điểm đã gửi duyệt; cần trả lại trước khi sửa.");Require(attempt.Status!=AttemptStatus.InProgress,"Bài chưa được nộp.");
        var previous=attempt.FinalScore;ApplyGrades(quiz,attempt,command.Answers);item.Revision++;Log(actor,item.Id,"attempt.grade",new{id,previous,attempt.FinalScore,command.Answers});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    private async Task<IReadOnlyList<GradebookRow>> BuildGrades(TeachingClass item,CancellationToken ct)
    {
        var quizzes=await db.Quizzes.AsNoTracking().Include(x=>x.Questions).Where(x=>x.TeachingClassId==item.Id && (x.Status==QuizStatus.Published || x.Status==QuizStatus.Closed)).OrderBy(x=>x.CreatedAt).ToListAsync(ct);
        var ids=quizzes.Select(x=>x.Id).ToList();var attempts=await db.QuizAttempts.AsNoTracking().Where(x=>ids.Contains(x.QuizId) && x.Status!=AttemptStatus.InProgress).ToListAsync(ct);
        var lessons=await db.Lessons.CountAsync(x=>x.CourseId==item.CourseId,ct);var roster=await (from e in db.OfferingEnrollments join u in db.Users on e.UserId equals u.Id where e.TeachingClassId==item.Id && e.Status!=EnrollmentStatus.Withdrawn select new{EnrollmentId=e.Id,u.FullName,StudentId=u.Id}).ToListAsync(ct);
        var result=new List<GradebookRow>();
        foreach(var row in roster)
        {
            var progress=lessons==0?0:await db.LessonCompletions.CountAsync(x=>x.EnrollmentId==row.EnrollmentId,ct)*100/lessons;
            var grades=quizzes.Select(q=>{var own=attempts.Where(x=>x.QuizId==q.Id && x.StudentId==row.StudentId).ToList();var best=own.Where(x=>x.FinalScore.HasValue).OrderByDescending(x=>x.FinalScore).ThenBy(x=>x.SubmittedAt).FirstOrDefault();return new AssessmentGrade(q.Id,q.Title,q.Weight,best?.Id,best?.FinalScore,q.Questions.Sum(x=>x.Points),own.Any(x=>!x.FinalScore.HasValue));}).ToList();
            var total=AssessmentRules.Total(grades);result.Add(new(row.StudentId,row.FullName,progress,total,total>=item.PassingGrade && progress>=item.RequiredProgress,grades));
        }
        return result;
    }
    public async Task<GradebookView> GradebookAsync(Guid id,Actor actor,CancellationToken ct)
    {
        var item=await Offering(id,actor,ct,false);var visible=actor.Role!=UserRole.Student || item.GradeStatus is GradebookStatus.Published or GradebookStatus.Locked;
        var rows=visible?await BuildGrades(item,ct):[];
        if(actor.Role==UserRole.Student)rows=rows.Where(x=>x.StudentId==actor.Id).ToList();
        return new(id,item.GradeStatus,item.Revision,visible,item.GradesPublishedAt,rows);
    }
    public async Task GradebookActionAsync(Guid id,WorkflowAction command,Actor actor,CancellationToken ct)
    {
        Reason(command.Reason);await using var tx=await db.Database.BeginTransactionAsync(ct);var item=await LockedOffering(id,actor,ct);Mutable(item);Version(item.Revision,command.Revision);
        if(!actor.IsHead && (actor.Role!=UserRole.Lecturer || item.PrimaryLecturerId!=actor.Id))throw new UnauthorizedAppException("Chỉ giảng viên chính hoặc Trưởng khoa được xử lý bảng điểm.");
        switch(command.Action)
        {
            case "submit":
                if(actor.Role!=UserRole.Lecturer || item.PrimaryLecturerId!=actor.Id)throw new UnauthorizedAppException("Chỉ giảng viên chính được gửi bảng điểm duyệt.");
                Require(item.Status==OfferingStatus.Grading && item.GradeStatus==GradebookStatus.Draft,"Chuyển lớp sang chấm điểm trước khi gửi duyệt.");
                var quizzes=await db.Quizzes.Where(x=>x.TeachingClassId==id).ToListAsync(ct);
                Require(quizzes.Count>0 && quizzes.All(x=>x.Status==QuizStatus.Closed) && quizzes.Sum(x=>x.Weight)==100,"Tất cả đề phải đóng và tổng trọng số đúng 100%.");
                Require(!await db.QuizAttempts.AnyAsync(x=>quizzes.Select(q=>q.Id).Contains(x.QuizId) && (x.Status!=AttemptStatus.Graded || !x.FinalScore.HasValue),ct),"Còn bài đang làm hoặc chưa chấm xong.");
                item.GradeStatus=GradebookStatus.PendingApproval;break;
            case "approve":
            {
                Head(actor);Require(item.GradeStatus==GradebookStatus.PendingApproval,"Bảng điểm chưa chờ duyệt.");
                var submittedBy=await db.AuditEntries.Where(x=>x.EntityId==id && x.Action=="gradebook.submit").OrderByDescending(x=>x.At).Select(x=>(Guid?)x.ActorId).FirstOrDefaultAsync(ct);
                Require(!submittedBy.HasValue || submittedBy.Value!=actor.Id,"Người gửi bảng điểm không được tự duyệt.");item.GradeStatus=GradebookStatus.Approved;break;
            }
            case "reject":Head(actor);Require(item.GradeStatus is GradebookStatus.PendingApproval or GradebookStatus.Approved,"Bảng điểm chưa gửi duyệt.");item.GradeStatus=GradebookStatus.Draft;break;
            case "publish":Head(actor);Require(item.GradeStatus==GradebookStatus.Approved,"Bảng điểm chưa được duyệt.");item.GradeStatus=GradebookStatus.Published;item.GradesPublishedAt=DateTime.UtcNow;break;
            default:throw new ConflictException("Thao tác bảng điểm không hợp lệ.");
        }
        item.Revision++;Log(actor,id,"gradebook."+command.Action,command);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task<IReadOnlyList<AppealView>> AppealsAsync(Guid id,Actor actor,CancellationToken ct)
    {
        await Offering(id,actor,ct,false);var query=db.GradeAppeals.AsNoTracking().Where(x=>x.TeachingClassId==id);
        if(actor.Role==UserRole.Student)query=query.Where(x=>x.StudentId==actor.Id);
        return await (from x in query join u in db.Users on x.StudentId equals u.Id orderby x.CreatedAt descending select new AppealView(x.Id,x.StudentId,u.FullName,x.AttemptId,x.Reason,x.Resolution,x.ReviewerId,x.Status,x.Revision,actor.Role==UserRole.Student?"[]":x.ProposedGradesJson)).ToListAsync(ct);
    }
    public async Task CreateAppealAsync(Guid id,AppealRequest command,Actor actor,CancellationToken ct)
    {
        Require(actor.Role==UserRole.Student,"Chỉ học viên gửi yêu cầu phúc khảo.");Reason(command.Reason);
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(id,ct);var item=await Offering(id,actor,ct,false);
        Require(item.GradeStatus==GradebookStatus.Published && item.GradesPublishedAt?.AddDays(item.AppealDays)>DateTime.UtcNow,"Không trong thời hạn phúc khảo.");
        var attempt=await Attempt(command.AttemptId,ct);Require(attempt.StudentId==actor.Id && await db.Quizzes.AnyAsync(x=>x.Id==attempt.QuizId && x.TeachingClassId==id,ct),"Bài nộp không thuộc lớp hoặc học viên.");
        Require(!await db.GradeAppeals.AnyAsync(x=>x.AttemptId==attempt.Id && (x.Status==AppealStatus.Pending || x.Status==AppealStatus.Reviewed),ct),"Đã có yêu cầu đang xử lý.");
        db.GradeAppeals.Add(new(){TeachingClassId=id,StudentId=actor.Id,AttemptId=attempt.Id,Reason=command.Reason});Log(actor,id,"appeal.create",command);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task ResolveAppealAsync(Guid id,AppealDecision command,Actor actor,CancellationToken ct)
    {
        Reason(command.Reason);var appeal=await db.GradeAppeals.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new NotFoundException("Không có yêu cầu phúc khảo.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);var item=await LockedOffering(appeal.TeachingClassId,actor,ct);await db.Entry(appeal).ReloadAsync(ct);Mutable(item);Version(appeal.Revision,command.Revision);
        Require(item.GradeStatus==GradebookStatus.Published,"Bảng điểm chưa công bố.");
        switch(command.Action)
        {
            case "assign":
                Head(actor);Require(appeal.Status==AppealStatus.Pending,"Yêu cầu không chờ xử lý.");
                Require(command.ReviewerId.HasValue && new[]{item.PrimaryLecturerId,item.AssistantLecturerOneId,item.AssistantLecturerTwoId}.Contains(command.ReviewerId.Value),"Chọn giảng viên trong tổ được phân công.");
                var gradedBy=await db.AuditEntries.Where(x=>x.EntityId==item.Id && x.Action=="attempt.grade").ToListAsync(ct);
                Require(!gradedBy.Any(x=>x.ActorId==command.ReviewerId && x.Detail.Contains(appeal.AttemptId.ToString())),"Người chấm ban đầu không được chấm phúc khảo bài này.");
                appeal.ReviewerId=command.ReviewerId;break;
            case "propose":
                Require(appeal.Status==AppealStatus.Pending && appeal.ReviewerId==actor.Id,"Bạn không được giao chấm lại.");
                var original=await Attempt(appeal.AttemptId,ct);var quiz=await Quiz(original.QuizId,ct);
                var proposal=command.Answers??[];
                // Validate against a detached copy; the official grade is unchanged until approval.
                var copy=new QuizAttempt{Answers=original.Answers.Select(x=>new QuizAnswer{QuestionId=x.QuestionId,Answer=x.Answer}).ToList()};ApplyGrades(quiz,copy,proposal);
                appeal.ProposedGradesJson=JsonSerializer.Serialize(proposal,JsonOptions);appeal.Status=AppealStatus.Reviewed;break;
            case "approve":
                Head(actor);Require(appeal.Status==AppealStatus.Reviewed && appeal.ReviewerId!=actor.Id,"Cần người khác duyệt đề xuất chấm lại.");
                var attempt=await Attempt(appeal.AttemptId,ct);var assessment=await Quiz(attempt.QuizId,ct);var old=attempt.FinalScore;
                ApplyGrades(assessment,attempt,JsonSerializer.Deserialize<List<GradeAnswer>>(appeal.ProposedGradesJson,JsonOptions)??[]);
                Log(actor,item.Id,"grade.correct",new{appeal.AttemptId,previous=old,current=attempt.FinalScore,command.Reason});appeal.Status=AppealStatus.Resolved;
                item.GradesPublishedAt=DateTime.UtcNow;break;
            case "reject":Head(actor);Require(appeal.Status is AppealStatus.Pending or AppealStatus.Reviewed,"Yêu cầu đã xử lý.");appeal.Status=AppealStatus.Rejected;break;
            default:throw new ConflictException("Thao tác phúc khảo không hợp lệ.");
        }
        appeal.Resolution=command.Reason;appeal.Revision++;item.Revision++;Log(actor,item.Id,"appeal."+command.Action,new{id,command.Reason,command.ReviewerId});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task<IReadOnlyList<AuditEntry>> AuditAsync(Guid id,Actor actor,CancellationToken ct){await Offering(id,actor,ct);return await db.AuditEntries.AsNoTracking().Where(x=>x.EntityId==id).OrderByDescending(x=>x.At).Take(500).ToListAsync(ct);}
    public async Task<string> ExportAsync(Guid id,Actor actor,CancellationToken ct)
    {
        Head(actor);var item=await Offering(id,actor,ct);var grades=await BuildGrades(item,ct);var roster=await RosterAsync(id,actor,ct);var quizzes=await db.Quizzes.AsNoTracking().Include(x=>x.Questions).Where(x=>x.TeachingClassId==id).ToListAsync(ct);var ids=quizzes.Select(x=>x.Id).ToList();
        var attempts=await db.QuizAttempts.AsNoTracking().Include(x=>x.Answers).Where(x=>ids.Contains(x.QuizId)).ToListAsync(ct);var audit=await db.AuditEntries.AsNoTracking().Where(x=>x.EntityId==id).OrderBy(x=>x.At).ToListAsync(ct);
        var content=await db.Courses.AsNoTracking().Include(x=>x.Lessons).SingleAsync(x=>x.Id==item.CourseId,ct);
        var appeals=await db.GradeAppeals.AsNoTracking().Where(x=>x.TeachingClassId==id).ToListAsync(ct);
        return JsonSerializer.Serialize(new{exportedAt=DateTime.UtcNow,offering=item,content=new{content.Code,content.Title,content.Description,lessons=content.Lessons.Select(x=>new{x.Id,x.Title,x.Content,x.Position,x.DurationMinutes})},roster,grades,quizzes=quizzes.Select(x=>new{x.Id,x.Title,x.Weight,x.OpensAt,x.ClosesAt,x.ShuffleQuestions,x.ShuffleOptions,questions=x.Questions.Select(q=>new{q.Id,q.Content,q.Type,q.Points,q.OptionsJson,q.CorrectAnswer})}),attempts=attempts.Select(x=>new{x.Id,x.QuizId,x.StudentId,x.StartedAt,x.SubmittedAt,x.ExpiresAt,x.Status,x.FinalScore,x.QuestionOrderJson,x.OptionOrderJson,answers=x.Answers.Select(a=>new{a.QuestionId,a.Answer,a.Score,a.Feedback})}),appeals,audit},JsonOptions);
    }
    public async Task<IReadOnlyList<AccountView>> AccountsAsync(Actor actor,CancellationToken ct){Admin(actor);return await db.Users.AsNoTracking().OrderBy(x=>x.FullName).Select(x=>new AccountView(x.Id,x.FullName,x.Email,x.Role,x.IsActive,x.MustChangePassword)).ToListAsync(ct);}
    public async Task<InitialCredential> CreateAccountAsync(CreateAccount command,Actor actor,CancellationToken ct)
    {
        Admin(actor);Require(!string.IsNullOrWhiteSpace(command.FullName) && command.FullName.Length<=200 && command.Email.Length<=256 && new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(command.Email) && Enum.IsDefined(command.Role),"Thông tin tài khoản không hợp lệ.");
        Require(command.Role!=UserRole.Student || !string.IsNullOrWhiteSpace(command.ClassCode) && command.ClassCode.Length<=30,"Học viên cần mã lớp hợp lệ.");
        var email=command.Email.Trim().ToLowerInvariant();Require(!await db.Users.AnyAsync(x=>x.Email==email,ct),"Email đã được sử dụng.");
        var password=Convert.ToHexString(RandomNumberGenerator.GetBytes(12));var user=new AppUser{FullName=command.FullName.Trim(),Email=email,Role=command.Role,ClassCode=command.ClassCode?.Trim().ToUpperInvariant(),MustChangePassword=true};
        user.PasswordHash=new PasswordHasher<AppUser>().HashPassword(user,password);db.Users.Add(user);Log(actor,user.Id,"account.create",new{user.Email,user.Role});await db.SaveChangesAsync(ct);return new(email,password);
    }
    public async Task<InitialCredential> ResetPasswordAsync(Guid id,Actor actor,CancellationToken ct)
    {
        Admin(actor);var user=await db.Users.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new NotFoundException("Không có tài khoản.");
        Require(user.Id!=actor.Id,"Dùng chức năng đổi mật khẩu cho tài khoản của chính bạn.");var password=Convert.ToHexString(RandomNumberGenerator.GetBytes(12));user.PasswordHash=new PasswordHasher<AppUser>().HashPassword(user,password);user.MustChangePassword=true;user.TokenVersion++;
        Log(actor,id,"account.reset",new{user.Email});await db.SaveChangesAsync(ct);return new(user.Email,password);
    }
    public async Task SetAccountActiveAsync(Guid id,bool active,Actor actor,CancellationToken ct)
    {
        Admin(actor);Require(id!=actor.Id,"Không thể tự khóa tài khoản.");var user=await db.Users.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new NotFoundException("Không có tài khoản.");user.IsActive=active;user.TokenVersion++;Log(actor,id,"account.active",new{active});await db.SaveChangesAsync(ct);
    }
    public async Task<AuthResult> ChangePasswordAsync(PasswordChange command,Actor actor,CancellationToken ct)
    {
        var user=await db.Users.SingleAsync(x=>x.Id==actor.Id,ct);var hasher=new PasswordHasher<AppUser>();
        Require(user.IsActive && hasher.VerifyHashedPassword(user,user.PasswordHash,command.CurrentPassword)!=PasswordVerificationResult.Failed,"Mật khẩu hiện tại không đúng.");
        Require(command.NewPassword.Length is >=12 and <=128 && command.NewPassword!=command.CurrentPassword,"Mật khẩu mới phải khác mật khẩu cũ và có 12–128 ký tự.");
        user.PasswordHash=hasher.HashPassword(user,command.NewPassword);user.MustChangePassword=false;user.TokenVersion++;Log(actor,user.Id,"account.password",new{});await db.SaveChangesAsync(ct);
        return new(tokens.Create(user),new UserDto(user.Id,user.FullName,user.Email,user.Role.ToString(),user.MustChangePassword));
    }
    public async Task SetAllowanceAsync(Guid quizId,AllowanceRequest command,Actor actor,CancellationToken ct)
    {
        Head(actor);Reason(command.Reason);var quiz=await Quiz(quizId,ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);var item=await LockedOffering(quiz.TeachingClassId!.Value,actor,ct);Mutable(item);
        Require(item.Status==OfferingStatus.Active && item.GradeStatus==GradebookStatus.Draft && quiz.Status==QuizStatus.Published,"Chỉ gia hạn khi lớp và bài kiểm tra đang mở.");
        var until=command.ExtendedUntil?.ToUniversalTime();
        Require(command.ExtraAttempts is >=0 and <=10 && (!until.HasValue || until>quiz.ClosesAt && until<=item.EndsAt),"Gia hạn phải sau hạn đề và trong lịch lớp; tối đa 10 lượt bổ sung.");
        Require(await db.OfferingEnrollments.AnyAsync(x=>x.TeachingClassId==item.Id && x.UserId==command.StudentId && x.Status==EnrollmentStatus.Active,ct),"Học viên chưa ghi danh trong lớp.");
        var allowance=await db.QuizAllowances.SingleOrDefaultAsync(x=>x.QuizId==quizId && x.StudentId==command.StudentId,ct);
        if(allowance is null){allowance=new(){QuizId=quizId,StudentId=command.StudentId};db.QuizAllowances.Add(allowance);}
        allowance.ExtraAttempts=command.ExtraAttempts;allowance.ExtendedUntil=until;
        var attempt=await db.QuizAttempts.SingleOrDefaultAsync(x=>x.QuizId==quizId && x.StudentId==command.StudentId && x.Status==AttemptStatus.InProgress,ct);
        if(attempt is not null && until.HasValue){attempt.ExpiresAt=until.Value;attempt.Revision++;}
        item.Revision++;Log(actor,item.Id,"quiz.allowance",new{quizId,command.StudentId,command.ExtraAttempts,until,command.Reason});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task<IReadOnlyList<Notification>> NotificationsAsync(Actor actor,CancellationToken ct) => await db.Notifications.AsNoTracking().Where(x=>x.UserId==actor.Id).OrderByDescending(x=>x.CreatedAt).Take(50).ToListAsync(ct);
    public async Task ReadNotificationAsync(Guid id,Actor actor,CancellationToken ct)
    {
        var note=await db.Notifications.SingleOrDefaultAsync(x=>x.Id==id && x.UserId==actor.Id,ct)??throw new NotFoundException("Không có thông báo.");note.ReadAt=DateTime.UtcNow;await db.SaveChangesAsync(ct);
    }
    public async Task GenerateNotificationsAsync(CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(Guid.Empty,ct);
        var now=DateTime.UtcNow;var heads=await db.Users.Where(x=>x.IsActive && (x.Role==UserRole.Admin || x.Role==UserRole.DepartmentHead)).Select(x=>x.Id).ToListAsync(ct);
        async Task Add(Guid userId,Guid offeringId,string key,string message)
        {if(!await db.Notifications.AnyAsync(x=>x.UserId==userId && x.DedupKey==key,ct))db.Notifications.Add(new(){UserId=userId,TeachingClassId=offeringId,DedupKey=key,Message=message});}
        foreach(var quiz in await db.Quizzes.AsNoTracking().Where(x=>x.TeachingClassId!=null && (x.Status==QuizStatus.PendingReview || x.Status==QuizStatus.Published)).ToListAsync(ct))
        {
            if(quiz.Status==QuizStatus.PendingReview){foreach(var h in heads)await Add(h,quiz.TeachingClassId!.Value,"review:"+quiz.Id+":"+quiz.Revision,"Có đề cần duyệt: "+quiz.Title);}
            else if(quiz.ClosesAt>now && quiz.ClosesAt<=now.AddHours(24))
            {
                var students=await db.OfferingEnrollments.Where(x=>x.TeachingClassId==quiz.TeachingClassId && x.Status==EnrollmentStatus.Active).Select(x=>x.UserId).ToListAsync(ct);
                foreach(var student in students)if(!await db.QuizAttempts.AnyAsync(x=>x.QuizId==quiz.Id && x.StudentId==student && x.Status!=AttemptStatus.InProgress,ct))await Add(student,quiz.TeachingClassId!.Value,"due:"+quiz.Id,"Sắp đến hạn bài kiểm tra: "+quiz.Title+" ("+quiz.ClosesAt!.Value.ToString("O")+").");
            }
        }
        foreach(var item in await db.TeachingClasses.AsNoTracking().Where(x=>x.Status==OfferingStatus.Active || x.Status==OfferingStatus.Grading).ToListAsync(ct))
        {
            if(item.GradeStatus==GradebookStatus.PendingApproval)foreach(var h in heads)await Add(h,item.Id,"grades:"+item.Id+":"+item.Revision,"Bảng điểm cần duyệt: "+item.CourseTitle+" · "+item.Code);
            if(item.Status==OfferingStatus.Active && item.StartsAt<=now.AddDays(-7))
            foreach(var e in await db.OfferingEnrollments.AsNoTracking().Where(x=>x.TeachingClassId==item.Id && x.Status==EnrollmentStatus.Active).ToListAsync(ct))
            {
                var recentLesson=await db.LessonCompletions.AnyAsync(x=>x.EnrollmentId==e.Id && x.CompletedAt>now.AddDays(-7),ct);
                var recentAttempt=await db.QuizAttempts.AnyAsync(x=>x.StudentId==e.UserId && x.StartedAt>now.AddDays(-7) && db.Quizzes.Any(q=>q.Id==x.QuizId && q.TeachingClassId==item.Id),ct);
                if(!recentLesson && !recentAttempt){var week=now.ToString("yyyy")+"-"+System.Globalization.ISOWeek.GetWeekOfYear(now);await Add(e.UserId,item.Id,"inactive:"+e.Id+":"+week,"Chưa ghi nhận hoàn thành bài học hoặc lượt kiểm tra trong 7 ngày: "+item.CourseTitle);}
            }
        }
        foreach (var scheduled in await db.ScheduleEvents.AsNoTracking().Where(x => !x.IsCancelled && x.StartsAt > now && x.StartsAt <= now.AddHours(24)).ToListAsync(ct))
        {
            var offering = await db.TeachingClasses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == scheduled.TeachingClassId, ct);
            if (offering is null) continue;
            var recipients = await db.OfferingEnrollments.AsNoTracking().Where(x => x.TeachingClassId == offering.Id && x.Status == EnrollmentStatus.Active).Select(x => x.UserId).ToListAsync(ct);
            recipients.AddRange([offering.PrimaryLecturerId, offering.AssistantLecturerOneId, offering.AssistantLecturerTwoId]);
            var reminder = $"Sắp đến lịch {(scheduled.Type == ScheduleEventType.Exam ? "thi" : "học")}: {scheduled.Title}, {scheduled.StartsAt.ToUniversalTime():dd/MM HH:mm} UTC, phòng {scheduled.Room}.";
            foreach (var userId in recipients.Distinct())
                await Add(userId, offering.Id, $"schedule-reminder:{scheduled.Id}:{scheduled.Revision}", reminder);
        }
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
}
