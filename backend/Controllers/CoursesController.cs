using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using LawLms.Application;
using LawLms.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawLms.Api.Controllers;

[ApiController, Route("api/courses")]
public sealed class CoursesController(ICourseService courses) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<IReadOnlyList<CourseSummary>>> GetAll(CancellationToken ct) => Ok(await courses.GetPublishedAsync(ct));
    [Authorize]
    [HttpGet("{id:guid}")] public async Task<ActionResult<CourseDetail>> Get(Guid id, CancellationToken ct) => Ok(await courses.GetAsync(id, UserId(), Enum.Parse<UserRole>(User.FindFirstValue(ClaimTypes.Role)!), ct));
    [Authorize(Roles = "DepartmentHead,Admin")]
    [HttpPost("{id:guid}/copy")] public async Task<IActionResult> Copy(Guid id,CopyCourseRequest request,CancellationToken ct)=>Ok(await courses.CloneAsync(id,UserId(),request.Code,ct));
    [Authorize(Roles = "DepartmentHead,Admin")]
    [HttpGet("management")] public async Task<ActionResult<IReadOnlyList<CourseSummary>>> Management(CancellationToken ct) => Ok(await courses.GetManagementAsync(ct));
    [Authorize(Roles = "Lecturer,DepartmentHead,Admin")]
    [HttpPost("{id:guid}/review")] public async Task<IActionResult> Review(Guid id, ReviewCourseRequest request, CancellationToken ct)
    {
        await courses.ReviewAsync(id, UserId(), Enum.Parse<UserRole>(User.FindFirstValue(ClaimTypes.Role)!), request.Action, request.Reason, request.Revision, ct);
        return NoContent();
    }
    [Authorize(Roles = "Lecturer,Admin")]
    [HttpGet("teaching")] public async Task<ActionResult<IReadOnlyList<CourseSummary>>> Teaching(CancellationToken ct) => Ok(await courses.GetTeachingAsync(UserId(), ct));
    [Authorize(Roles = "DepartmentHead,Admin")]
    [HttpPost] public async Task<ActionResult<CourseDetail>> Create(CreateCourseRequest request, CancellationToken ct) { var course = await courses.CreateAsync(UserId(), new CreateCourseCommand(request.Code, request.Title, request.Description, request.Category, request.Color), ct); return CreatedAtAction(nameof(Get), new { id = course.Id }, course); }
    [Authorize(Roles = "DepartmentHead,Admin")]
    [HttpPut("{id:guid}")] public async Task<ActionResult<CourseDetail>> Update(Guid id, CreateCourseRequest request, CancellationToken ct) => Ok(await courses.UpdateAsync(id, new CreateCourseCommand(request.Code, request.Title, request.Description, request.Category, request.Color), ct));
    [Authorize(Roles = "DepartmentHead,Admin")]
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await courses.DeleteAsync(id, ct); return NoContent(); }
    [Authorize(Roles = "Lecturer,DepartmentHead,Admin")]
    [HttpPost("{id:guid}/lessons")] public async Task<ActionResult<LessonDto>> AddLesson(Guid id, CreateLessonRequest request, CancellationToken ct) => Ok(await courses.AddLessonAsync(UserId(), User.IsInRole("Admin") || User.IsInRole("DepartmentHead"), id, new CreateLessonCommand(request.Title, request.Content, request.DurationMinutes), ct));
    [Authorize(Roles = "Lecturer,DepartmentHead,Admin")]
    [HttpPut("{id:guid}/lessons/{lessonId:guid}")] public async Task<IActionResult> UpdateLesson(Guid id,Guid lessonId,UpdateLessonRequest request,CancellationToken ct){await courses.UpdateLessonAsync(UserId(),User.IsInRole("Admin")||User.IsInRole("DepartmentHead"),id,lessonId,new(request.Title,request.Content,request.DurationMinutes),request.Revision,ct);return NoContent();}
    [Authorize]
    [HttpGet("my-learning")] public async Task<ActionResult<IReadOnlyList<LearningSummary>>> MyLearning(CancellationToken ct) => Ok(await courses.GetMyLearningAsync(UserId(), ct));
    private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public sealed record ReviewCourseRequest([param: Required, RegularExpression("^(submit|publish|reject)$")] string Action, [param: Required(AllowEmptyStrings = true), StringLength(2000)] string Reason, [param: Range(0, int.MaxValue)] int Revision);

public sealed record CreateCourseRequest([param: Required, StringLength(30)] string Code, [param: Required, StringLength(300)] string Title, [param: Required, StringLength(4000)] string Description, [param: Required, StringLength(100)] string Category, [param: RegularExpression("^#[0-9A-Fa-f]{6}$")] string Color);
public sealed record CreateLessonRequest([param: Required, StringLength(300)] string Title, [param: Required, StringLength(20000)] string Content, [param: Range(1, 600)] int DurationMinutes);

public record CopyCourseRequest([param: Required, StringLength(30)] string Code);

public record UpdateLessonRequest([param: Required, StringLength(300)] string Title,[param: Required, StringLength(20000)] string Content,[param: Range(1,600)] int DurationMinutes,[param: Range(0,int.MaxValue)] int Revision);
