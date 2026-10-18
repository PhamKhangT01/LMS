using System.Text.Json;
using LawLms.Application;
using LawLms.Domain;
using LawLms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LawLms.Infrastructure.Services;

public sealed class QuestionBankService(LmsDbContext db) : IQuestionBankService
{
    private static void Lecturer(Actor actor)
    { if (actor.Role != UserRole.Lecturer) throw new UnauthorizedAppException("Chỉ giảng viên được phân công mới được quản lý câu hỏi."); }
    private IQueryable<Course> Scope(Actor actor)
    {
        if (actor.IsHead) return db.Courses.AsNoTracking();
        if (actor.Role != UserRole.Lecturer) throw new UnauthorizedAppException("Bạn không được truy cập ngân hàng câu hỏi.");
        return db.Courses.AsNoTracking().Where(c => db.TeachingClasses.Any(t => t.CourseId == c.Id && (t.PrimaryLecturerId == actor.Id || t.AssistantLecturerOneId == actor.Id || t.AssistantLecturerTwoId == actor.Id)));
    }
    private async Task Access(Guid courseId, Actor actor, CancellationToken ct)
    { if (!await Scope(actor).AnyAsync(c => c.Id == courseId, ct)) throw new UnauthorizedAppException("Bạn không được phân công học phần này."); }
    public async Task<IReadOnlyList<BankCourse>> CoursesAsync(Actor actor, CancellationToken ct) => await Scope(actor).OrderBy(c => c.Code).Select(c => new BankCourse(c.Id, c.Code, c.Title)).ToListAsync(ct);
    private static IQueryable<BankQuestion> Filter(IQueryable<BankQuestion> rows, string? topic, QuestionType? type, QuestionDifficulty? difficulty)
    {
        if (type.HasValue && !Enum.IsDefined(type.Value) || difficulty.HasValue && !Enum.IsDefined(difficulty.Value)) throw new ConflictException("Bộ lọc câu hỏi không hợp lệ.");
        if (!string.IsNullOrWhiteSpace(topic)) rows = rows.Where(x => x.Topic.ToLower().Contains(topic.Trim().ToLower()));
        if (type.HasValue) rows = rows.Where(x => x.Type == type.Value);
        if (difficulty.HasValue) rows = rows.Where(x => x.Difficulty == difficulty.Value);
        return rows;
    }
    private async Task<IReadOnlyList<BankQuestionView>> Views(IReadOnlyList<BankQuestion> rows, CancellationToken ct)
    {
        var ids = rows.Select(x => x.CreatedById).Distinct().ToArray();
        var names = await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        return rows.Select(x => new BankQuestionView(x.Id,x.CourseId,x.CreatedById,names.GetValueOrDefault(x.CreatedById,"Không xác định"),x.Content,x.Type,JsonSerializer.Deserialize<List<string>>(x.OptionsJson)??[],x.CorrectAnswer,x.Points,x.Topic,x.Difficulty,x.Source,x.IsArchived,x.Revision)).ToList();
    }
    public async Task<BankPage> SearchAsync(BankFilter filter, Actor actor, CancellationToken ct)
    {
        await Access(filter.CourseId,actor,ct);
        if (filter.Page < 1 || filter.PageSize is < 1 or > 100 || (long)(filter.Page-1)*filter.PageSize>int.MaxValue) throw new ConflictException("Phân trang không hợp lệ.");
        var rows = Filter(db.BankQuestions.AsNoTracking().Where(x=>x.CourseId==filter.CourseId),filter.Topic,filter.Type,filter.Difficulty);
        if (!filter.IncludeArchived) rows=rows.Where(x=>!x.IsArchived);
        if (!string.IsNullOrWhiteSpace(filter.Query)) { var text=filter.Query.Trim().ToLower(); rows=rows.Where(x=>x.Content.ToLower().Contains(text) || x.Topic.ToLower().Contains(text)); }
        var total=await rows.CountAsync(ct);
        var items=await rows.OrderByDescending(x=>x.UpdatedAt).ThenBy(x=>x.Id).Skip((filter.Page-1)*filter.PageSize).Take(filter.PageSize).ToListAsync(ct);
        return new(await Views(items,ct),total,filter.Page,filter.PageSize);
    }
    private static CreateQuizQuestionCommand Normalize(CreateQuizQuestionCommand q) => q with { Content=q.Content.Trim(), Options=q.Options.Select(x=>x.Trim()).ToArray(), CorrectAnswer=q.CorrectAnswer.Trim() };
    private static BankQuestion Make(Guid courseId, Guid ownerId, CreateQuizQuestionCommand q, string topic, QuestionDifficulty difficulty, BankQuestionSource source) => new() { CourseId=courseId,CreatedById=ownerId,Content=q.Content,Type=q.Type,OptionsJson=JsonSerializer.Serialize(q.Options),CorrectAnswer=q.CorrectAnswer,Points=q.Points,Topic=topic.Trim(),Difficulty=difficulty,Source=source,Fingerprint=BankQuestionRules.Fingerprint(q) };
    private void Audit(Actor actor, BankQuestion row, string action) => db.AuditEntries.Add(new() {ActorId=actor.Id,EntityId=row.CourseId,Action=action,Detail=JsonSerializer.Serialize(new {questionId=row.Id,row.Revision})});
    public async Task<Guid> CreateEssayAsync(BankEssay command, Actor actor, CancellationToken ct)
    {
        Lecturer(actor); await Access(command.CourseId,actor,ct);
        var q=Normalize(new(command.Content,QuestionType.Essay,[],command.GradingGuide,command.Points));
        BankQuestionRules.Validate(q,command.Topic,command.Difficulty);
        var row=Make(command.CourseId,actor.Id,q,command.Topic,command.Difficulty,BankQuestionSource.ManualEssay);
        if(await db.BankQuestions.AnyAsync(x=>x.CourseId==row.CourseId&&x.CreatedById==actor.Id&&x.Fingerprint==row.Fingerprint,ct)) throw new ConflictException("Câu hỏi này đã có trong ngân hàng, kể cả mục đã lưu trữ.");
        db.BankQuestions.Add(row); Audit(actor,row,"bank.create"); await db.SaveChangesAsync(ct); return row.Id;
    }
    private async Task<BankQuestion> Owned(Guid id, int revision, Actor actor, CancellationToken ct)
    {
        Lecturer(actor); var row=await db.BankQuestions.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new NotFoundException("Không tìm thấy câu hỏi.");
        await Access(row.CourseId,actor,ct);
        if(row.CreatedById!=actor.Id)throw new UnauthorizedAppException("Chỉ người tạo được sửa hoặc lưu trữ câu hỏi này.");
        if(row.Revision!=revision)throw new ConflictException("Câu hỏi đã thay đổi. Hãy tải lại trước khi xử lý.");
        return row;
    }
    public async Task EditAsync(Guid id, BankEdit command, Actor actor, CancellationToken ct)
    {
        var row=await Owned(id,command.Revision,actor,ct);
        if(row.IsArchived)throw new ConflictException("Khôi phục câu hỏi trước khi chỉnh sửa.");
        var q=Normalize(new(command.Content,row.Type,command.Options,command.CorrectAnswer,command.Points));
        BankQuestionRules.Validate(q,command.Topic,command.Difficulty);
        row.Content=q.Content;row.OptionsJson=JsonSerializer.Serialize(q.Options);row.CorrectAnswer=q.CorrectAnswer;row.Points=q.Points;row.Topic=command.Topic.Trim();row.Difficulty=command.Difficulty;row.Fingerprint=BankQuestionRules.Fingerprint(q);row.Revision++;row.UpdatedAt=DateTime.UtcNow;
        Audit(actor,row,"bank.edit");await db.SaveChangesAsync(ct);
    }
    public async Task ArchiveAsync(Guid id, BankArchive command, Actor actor, CancellationToken ct)
    {
        var row=await Owned(id,command.Revision,actor,ct);row.IsArchived=command.Archived;row.Revision++;row.UpdatedAt=DateTime.UtcNow;
        Audit(actor,row,command.Archived?"bank.archive":"bank.restore");await db.SaveChangesAsync(ct);
    }
    public async Task<BankImportResult> ImportAsync(Guid courseId,string topic,QuestionDifficulty difficulty,IReadOnlyList<CreateQuizQuestionCommand> questions,BankQuestionSource source,Actor actor,CancellationToken ct)
    {
        Lecturer(actor);await Access(courseId,actor,ct);
        if(questions.Count is <1 or >200)throw new ConflictException("Mỗi lần nhập cần 1–200 câu hỏi.");
        var normalized=questions.Select(Normalize).ToList();foreach(var q in normalized)BankQuestionRules.Validate(q,topic,difficulty);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({$"bank:{courseId}:{actor.Id}"}))",ct);
        var fingerprints=(await db.BankQuestions.Where(x=>x.CourseId==courseId&&x.CreatedById==actor.Id).Select(x=>x.Fingerprint).ToListAsync(ct)).ToHashSet();
        var count=0;
        foreach(var q in normalized)
        {
            var row=Make(courseId,actor.Id,q,topic,difficulty,source);
            if(!fingerprints.Add(row.Fingerprint))continue;
            db.BankQuestions.Add(row);Audit(actor,row,"bank.import");count++;
        }
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return new(count,questions.Count-count);
    }
    public async Task<BankImportResult> FromQuizAsync(Guid quizId,BankFromQuiz command,Actor actor,CancellationToken ct)
    {
        Lecturer(actor);var quiz=await db.Quizzes.AsNoTracking().Include(x=>x.Questions).SingleOrDefaultAsync(x=>x.Id==quizId,ct)??throw new NotFoundException("Không tìm thấy đề kiểm tra.");
        return await ImportAsync(quiz.CourseId,command.Topic,command.Difficulty,quiz.Questions.OrderBy(x=>x.Position).Select(x=>new CreateQuizQuestionCommand(x.Content,x.Type,JsonSerializer.Deserialize<List<string>>(x.OptionsJson)??[],x.CorrectAnswer,x.Points)).ToList(),BankQuestionSource.ExistingQuiz,actor,ct);
    }
    public async Task<IReadOnlyList<BankQuestionView>> DrawAsync(BankDraw command,Actor actor,CancellationToken ct)
    {
        await Access(command.CourseId,actor,ct);
        if(command.Count is <1 or >200 || command.ExcludeIds?.Count>200)throw new ConflictException("Chọn ngẫu nhiên từ 1 đến 200 câu hỏi.");
        var rows=Filter(db.BankQuestions.AsNoTracking().Where(x=>x.CourseId==command.CourseId&&!x.IsArchived),command.Topic,command.Type,command.Difficulty);
        if(command.ExcludeIds is {Count:>0})rows=rows.Where(x=>!command.ExcludeIds.Contains(x.Id));
        var ids=await rows.Select(x=>x.Id).ToListAsync(ct);
        if(ids.Count<command.Count)throw new ConflictException($"Chỉ còn {ids.Count} câu hỏi phù hợp chưa được chọn.");
        QuizPresentation.Shuffle(ids);var chosen=ids.Take(command.Count).ToArray();
        var selected=await rows.Where(x=>chosen.Contains(x.Id)).ToListAsync(ct);
        var byId=selected.ToDictionary(x=>x.Id);return await Views(chosen.Select(id=>byId[id]).ToList(),ct);
    }
}
