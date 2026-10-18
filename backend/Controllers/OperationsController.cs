using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using LawLms.Application;
using LawLms.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawLms.Api.Controllers;

[ApiController, Authorize, Route("api/operations")]
public sealed class OperationsController(IOperationsService service) : ControllerBase
{
    private Actor Actor => new(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),Enum.Parse<UserRole>(User.FindFirstValue(ClaimTypes.Role)!));
    [HttpGet("notifications")] public async Task<IActionResult> Notifications(CancellationToken ct)=>Ok(await service.NotificationsAsync(Actor,ct));
    [HttpPost("notifications/{id:guid}/read")] public async Task<IActionResult> ReadNotification(Guid id,CancellationToken ct){await service.ReadNotificationAsync(id,Actor,ct);return NoContent();}
    [HttpPost("quizzes/{id:guid}/allowance")] public async Task<IActionResult> Allowance(Guid id,AllowanceRequest c,CancellationToken ct){await service.SetAllowanceAsync(id,c,Actor,ct);return NoContent();}
    [HttpGet("offerings")] public async Task<IActionResult> Offerings(CancellationToken ct) => Ok(await service.OfferingsAsync(Actor,ct));
    [HttpPut("offerings/{id:guid}/settings")] public async Task<IActionResult> Configure(Guid id,OfferingSettings c,CancellationToken ct){await service.ConfigureAsync(id,c,Actor,ct);return NoContent();}
    [HttpPost("offerings/{id:guid}/transition")] public async Task<IActionResult> Transition(Guid id,WorkflowAction c,CancellationToken ct){await service.TransitionAsync(id,c,Actor,ct);return NoContent();}
    [HttpGet("offerings/{id:guid}/roster")] public async Task<IActionResult> Roster(Guid id,CancellationToken ct)=>Ok(await service.RosterAsync(id,Actor,ct));
    [HttpPost("offerings/{id:guid}/sync")] public async Task<IActionResult> Sync(Guid id,ReasonRequest c,CancellationToken ct){await service.SyncRosterAsync(id,c.Reason,Actor,ct);return NoContent();}
    [HttpPost("offerings/{id:guid}/enrollments")] public async Task<IActionResult> Enrollment(Guid id,EnrollmentChange c,CancellationToken ct){await service.ChangeEnrollmentAsync(id,c,Actor,ct);return NoContent();}
    [HttpPost("offerings/{id:guid}/lessons/{lessonId:guid}/complete")] public async Task<IActionResult> Complete(Guid id,Guid lessonId,CancellationToken ct){await service.CompleteLessonAsync(id,lessonId,Actor,ct);return NoContent();}
    [HttpGet("offerings/{id:guid}/quizzes")] public async Task<IActionResult> Quizzes(Guid id,CancellationToken ct)=>Ok(await service.QuizzesAsync(id,Actor,ct));
    [HttpGet("quizzes/{id:guid}/draft")] public async Task<IActionResult> QuizDraft(Guid id,CancellationToken ct)=>Ok(await service.GetQuizDraftAsync(id,Actor,ct));
    [HttpPut("quizzes/{id:guid}")] public async Task<IActionResult> ReviseQuiz(Guid id,QuizDraft c,CancellationToken ct){await service.ReviseQuizAsync(id,c,Actor,ct);return NoContent();}
    [HttpDelete("quizzes/{id:guid}")] public async Task<IActionResult> RemoveQuiz(Guid id,[FromBody] WorkflowAction c,CancellationToken ct){await service.RemoveQuizDraftAsync(id,c,Actor,ct);return NoContent();}
    [HttpGet("quizzes/{id:guid}/preview")] public async Task<IActionResult> Preview(Guid id,CancellationToken ct)=>Ok(await service.PreviewQuizAsync(id,Actor,ct));
    [HttpPost("quizzes")] public async Task<IActionResult> CreateQuiz(QuizSetup c,CancellationToken ct)=>Ok(new{id=await service.CreateQuizAsync(c,Actor,ct)});
    [HttpPost("quizzes/{id:guid}/review")] public async Task<IActionResult> ReviewQuiz(Guid id,WorkflowAction c,CancellationToken ct){await service.ReviewQuizAsync(id,c,Actor,ct);return NoContent();}
    [HttpPost("quizzes/{id:guid}/start")] public async Task<IActionResult> Start(Guid id,CancellationToken ct)=>Ok(await service.StartAsync(id,Actor,ct));
    [HttpPut("attempts/{id:guid}/draft")] public async Task<IActionResult> Save(Guid id,SaveAttempt c,CancellationToken ct)=>Ok(await service.SaveAsync(id,c,false,Actor,ct));
    [HttpPost("attempts/{id:guid}/submit")] public async Task<IActionResult> Submit(Guid id,SaveAttempt c,CancellationToken ct)=>Ok(await service.SaveAsync(id,c,true,Actor,ct));
    [HttpGet("offerings/{id:guid}/grading")] public async Task<IActionResult> Grading(Guid id,CancellationToken ct)=>Ok(await service.GradingAsync(id,Actor,ct));
    [HttpGet("attempts/{id:guid}/result")] public async Task<IActionResult> Result(Guid id,CancellationToken ct)=>Ok(await service.ResultAsync(id,Actor,ct));
    [HttpPut("attempts/{id:guid}/grade")] public async Task<IActionResult> Grade(Guid id,GradeAttempt c,CancellationToken ct){await service.GradeAsync(id,c,Actor,ct);return NoContent();}
    [HttpGet("offerings/{id:guid}/grades")] public async Task<IActionResult> Grades(Guid id,CancellationToken ct)=>Ok(await service.GradebookAsync(id,Actor,ct));
    [HttpPost("offerings/{id:guid}/grades")] public async Task<IActionResult> GradeAction(Guid id,WorkflowAction c,CancellationToken ct){await service.GradebookActionAsync(id,c,Actor,ct);return NoContent();}
    [HttpGet("offerings/{id:guid}/appeals")] public async Task<IActionResult> Appeals(Guid id,CancellationToken ct)=>Ok(await service.AppealsAsync(id,Actor,ct));
    [HttpPost("offerings/{id:guid}/appeals")] public async Task<IActionResult> Appeal(Guid id,AppealRequest c,CancellationToken ct){await service.CreateAppealAsync(id,c,Actor,ct);return NoContent();}
    [HttpPost("appeals/{id:guid}")] public async Task<IActionResult> Resolve(Guid id,AppealDecision c,CancellationToken ct){await service.ResolveAppealAsync(id,c,Actor,ct);return NoContent();}
    [HttpGet("offerings/{id:guid}/audit")] public async Task<IActionResult> Audit(Guid id,CancellationToken ct)=>Ok(await service.AuditAsync(id,Actor,ct));
    [HttpGet("offerings/{id:guid}/export")] public async Task<IActionResult> Export(Guid id,CancellationToken ct)=>File(Encoding.UTF8.GetBytes(await service.ExportAsync(id,Actor,ct)),"application/json",$"ho-so-lop-{id}.json");
    [HttpPost("accounts")] public async Task<IActionResult> CreateAccount(CreateAccount c,CancellationToken ct)=>Ok(await service.CreateAccountAsync(c,Actor,ct));
    [HttpGet("accounts")] public async Task<IActionResult> Accounts(CancellationToken ct)=>Ok(await service.AccountsAsync(Actor,ct));
    [HttpPost("accounts/{id:guid}/reset")] public async Task<IActionResult> Reset(Guid id,CancellationToken ct)=>Ok(await service.ResetPasswordAsync(id,Actor,ct));
    [HttpPost("accounts/{id:guid}/active")] public async Task<IActionResult> Active(Guid id,ActiveRequest c,CancellationToken ct){await service.SetAccountActiveAsync(id,c.Active,Actor,ct);return NoContent();}
    [HttpPost("change-password")] public async Task<IActionResult> Change(PasswordChange c,CancellationToken ct)=>Ok(await service.ChangePasswordAsync(c,Actor,ct));
}
public record ReasonRequest([param:Required,StringLength(2000)] string Reason);
public record ActiveRequest(bool Active);
