using System.Security.Claims;
using System.Text;
using LawLms.Application;
using LawLms.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawLms.Api.Controllers;

[ApiController, Authorize(Roles = "Admin,DepartmentHead,Lecturer"), Route("api/operations/reports")]
public sealed class AcademicReportsController(IAcademicReportService service) : ControllerBase
{
    private Actor Actor => new(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), Enum.Parse<UserRole>(User.FindFirstValue(ClaimTypes.Role)!));
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] AcademicReportFilter filter, CancellationToken ct)
        => Ok(await service.GetAsync(filter, Actor, ct));
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] AcademicReportFilter filter, [FromQuery] string kind, CancellationToken ct)
    {
        var csv = await service.ExportAsync(filter, kind, Actor, ct);
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8", $"bao-cao-{kind}-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}
