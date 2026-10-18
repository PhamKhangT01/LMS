using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using LawLms.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawLms.Api.Controllers;

[ApiController, Route("api/teaching-classes")]
public sealed class TeachingClassesController(ITeachingClassService classes) : ControllerBase
{
    [Authorize(Roles = "DepartmentHead,Admin")]
    [HttpGet("lecturers")] public async Task<ActionResult<IReadOnlyList<LecturerDto>>> GetLecturers(CancellationToken ct) => Ok(await classes.GetLecturersAsync(ct));
    [Authorize(Roles = "DepartmentHead,Admin")]
    [HttpGet] public async Task<ActionResult<IReadOnlyList<TeachingClassDto>>> GetAll(CancellationToken ct) => Ok(await classes.GetAllAsync(ct));
    [Authorize(Roles = "DepartmentHead,Admin")]
    [HttpPost] public async Task<ActionResult<TeachingClassDto>> Assign(AssignTeachingClassRequest request, CancellationToken ct) { var result = await classes.AssignAsync(new AssignTeachingClassCommand(request.Code, request.Name, request.CourseId, request.AcademicYear, request.PrimaryLecturerId, request.AssistantLecturerOneId, request.AssistantLecturerTwoId, request.Term), ct); return CreatedAtAction(nameof(GetAll), result); }
    [Authorize(Roles = "DepartmentHead,Admin")]
    [HttpPut("{id:guid}")] public async Task<ActionResult<TeachingClassDto>> Update(Guid id, AssignTeachingClassRequest request, CancellationToken ct) => Ok(await classes.UpdateAsync(id, new AssignTeachingClassCommand(request.Code, request.Name, request.CourseId, request.AcademicYear, request.PrimaryLecturerId, request.AssistantLecturerOneId, request.AssistantLecturerTwoId, request.Term), ct));
    [Authorize(Roles = "DepartmentHead,Admin")]
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await classes.DeleteAsync(id, ct); return NoContent(); }
    [Authorize(Roles = "Lecturer")]
    [HttpGet("my-assignments")] public async Task<ActionResult<IReadOnlyList<TeachingClassDto>>> MyAssignments(CancellationToken ct) => Ok(await classes.GetForLecturerAsync(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), ct));
}

public sealed record AssignTeachingClassRequest([param: Required, StringLength(30)] string Code, [param: Required, StringLength(200)] string Name, Guid CourseId, [param: Required, StringLength(30)] string AcademicYear, Guid PrimaryLecturerId, Guid AssistantLecturerOneId, Guid AssistantLecturerTwoId, [param: Required, StringLength(30)] string Term = "HK1");
