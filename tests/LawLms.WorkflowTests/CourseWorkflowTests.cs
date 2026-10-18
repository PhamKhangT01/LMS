using System.Reflection;
using System.Text.Json;
using LawLms.Application;
using LawLms.Domain;
using Xunit;

public class CourseWorkflowTests
{
    private readonly Course course = new() { Title = "Luật", LecturerId = Guid.NewGuid() };
    private readonly Guid student = Guid.NewGuid();
    private readonly Guid assistant = Guid.NewGuid();
    private readonly Guid head = Guid.NewGuid();
    private bool enrolled;
    private readonly CourseService service;
    public CourseWorkflowTests()
    {
        service = new CourseService(
            Stub<ICourseRepository>.Create((name, _) => name switch {
                "GetDetailAsync" => Task.FromResult<Course?>(course),
                "AddAsync" or "AddLessonAsync" => Task.CompletedTask,
                _ => throw new NotImplementedException(name)
            }),
            Stub<ITeachingClassRepository>.Create((name, args) => name == "GetForLecturerAsync"
                ? Task.FromResult<IReadOnlyList<TeachingClass>>((Guid)args![0]! == assistant ? [new() { CourseId = course.Id }] : [])
                : throw new NotImplementedException(name)),
            Stub<IEnrollmentRepository>.Create((name, _) => name == "ExistsAsync" ? Task.FromResult(enrolled) : throw new NotImplementedException(name)),
            Stub<IQuizRepository>.Create((name, _) => throw new NotImplementedException(name)),
            Stub<IUnitOfWork>.Create((_, _) => Task.FromResult(1)));
    }
    private Task Review(string action, UserRole role = UserRole.DepartmentHead, string reason = "", int? revision = null, Guid? actor = null)
        => service.ReviewAsync(course.Id, actor ?? head, role, action, reason, revision ?? course.Revision, default);
    private void AddContent() => course.Lessons.Add(new() { Content = "Nội dung", Title = "Bài 1" });

    [Fact] public async Task NewCourseIsDraft()
    {
        var result = await service.CreateAsync(head, new("HP01", "Luật", "Mô tả", "Luật", "#355C42"), default);
        Assert.Equal(CourseStatus.Draft, result.Status);
    }
    [Fact] public async Task EmptyCourseCannotSubmit() => await Assert.ThrowsAsync<ConflictException>(() => Review("submit"));
    [Fact] public async Task AssistantSubmitsAndHeadPublishesWithHistory()
    {
        AddContent(); await Review("submit", UserRole.Lecturer, actor: assistant);
        Assert.False(course.IsPublished);
        await Review("publish");
        Assert.True(course.IsPublished); Assert.Equal(CourseStatus.Published, course.Status);
        var history = JsonSerializer.Deserialize<List<CourseReviewEvent>>(course.ReviewHistoryJson)!;
        Assert.Equal(2, history.Count); Assert.Equal(assistant, history[0].ActorId); Assert.Equal(head, history[1].ActorId);
    }
    [Fact] public async Task CannotSkipReview() { AddContent(); await Assert.ThrowsAsync<ConflictException>(() => Review("publish")); }
    [Fact] public async Task StudentCannotSubmit() { AddContent(); await Assert.ThrowsAsync<UnauthorizedAppException>(() => Review("submit", UserRole.Student)); }
    [Fact] public async Task UnassignedLecturerCannotSubmit() { AddContent(); await Assert.ThrowsAsync<UnauthorizedAppException>(() => Review("submit", UserRole.Lecturer)); }
    [Fact] public async Task LecturerCannotPublish() { AddContent(); await Review("submit"); await Assert.ThrowsAsync<ConflictException>(() => Review("publish", UserRole.Lecturer, actor: assistant)); }
    [Fact] public async Task RejectRequiresReasonAndUnlocksDraft()
    {
        AddContent(); await Review("submit");
        await Assert.ThrowsAsync<ConflictException>(() => Review("reject"));
        await Review("reject", reason: "Bổ sung nội dung");
        Assert.Equal(CourseStatus.Draft, course.Status); Assert.False(course.IsPublished);
    }
    [Fact] public async Task StaleDecisionRejected() { AddContent(); await Review("submit"); await Assert.ThrowsAsync<ConflictException>(() => Review("publish", revision: 0)); }
    [Theory] [InlineData(CourseStatus.PendingReview)] [InlineData(CourseStatus.Published)]
    public async Task ReviewedContentCannotBeEdited(CourseStatus status)
    {
        course.Status = status;
        AddContent();
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateLessonAsync(head,true,course.Id,course.Lessons.Single().Id,new("Sửa","Sửa",30),course.Revision,default));
        await Assert.ThrowsAsync<ConflictException>(() => service.AddLessonAsync(head, true, course.Id, new("Bài", "Nội dung", 45), default));
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(course.Id, new("HP", "Tên", "Mô tả", "Luật", "#355C42"), default));
    }
    [Fact] public async Task DraftLessonCanBeCorrected()
    {
        AddContent();var lesson=course.Lessons.Single();
        await service.UpdateLessonAsync(head,true,course.Id,lesson.Id,new("Đã sửa","Nội dung mới",30),0,default);
        Assert.Equal("Nội dung mới",lesson.Content);Assert.Equal(1,course.Revision);
        await Assert.ThrowsAsync<ConflictException>(()=>service.UpdateLessonAsync(head,true,course.Id,lesson.Id,new("Cũ","Cũ",20),0,default));
    }
    [Fact] public async Task StudentNeedsBothEnrollmentAndPublication()
    {
        enrolled = true;
        await Assert.ThrowsAsync<UnauthorizedAppException>(() => service.GetAsync(course.Id, student, UserRole.Student, default));
        course.IsPublished = true; course.Status = CourseStatus.Published; enrolled = false;
        await Assert.ThrowsAsync<UnauthorizedAppException>(() => service.GetAsync(course.Id, student, UserRole.Student, default));
        enrolled = true;
        course.ReviewHistoryJson = JsonSerializer.Serialize(new[] { new CourseReviewEvent(head, "publish", "Nhận xét nội bộ", DateTime.UtcNow) });
        var detail = await service.GetAsync(course.Id, student, UserRole.Student, default);
        Assert.Equal(course.Id, detail.Id); Assert.Empty(detail.ReviewHistory);
    }
    [Fact] public async Task OnlyAssignedLecturerCanReadDraft()
    {
        Assert.Equal(course.Id, (await service.GetAsync(course.Id, assistant, UserRole.Lecturer, default)).Id);
        await Assert.ThrowsAsync<UnauthorizedAppException>(() => service.GetAsync(course.Id, Guid.NewGuid(), UserRole.Lecturer, default));
    }
}
public class Stub<T> : DispatchProxy where T : class
{
    public Func<string, object?[]?, object> Handle { get; set; } = null!;
    public static T Create(Func<string, object?[]?, object> handler) { var result = DispatchProxy.Create<T, Stub<T>>(); ((Stub<T>)(object)result).Handle = handler; return result; }
    protected override object Invoke(MethodInfo? targetMethod, object?[]? args) => Handle(targetMethod!.Name, args);
}
