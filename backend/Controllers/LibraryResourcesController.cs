using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using LawLms.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawLms.Api.Controllers;

[ApiController, Authorize, Route("api/library-resources")]
public sealed class LibraryResourcesController(ILibraryResourceService resources) : ControllerBase
{
    private const long MaxFileSize = 25 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = [".pdf", ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx", ".zip"];
    [HttpGet] public async Task<ActionResult<IReadOnlyList<LibraryResourceDto>>> Search([FromQuery] string? query, CancellationToken ct) => Ok(await resources.SearchAsync(query, ct));
    [Authorize(Roles = "Lecturer,DepartmentHead,Admin")]
    [RequestSizeLimit(MaxFileSize)]
    [HttpPost] public async Task<ActionResult<LibraryResourceDto>> Upload([FromForm] UploadLibraryResourceRequest request, CancellationToken ct)
    {
        if (request.File is null || request.File.Length == 0) return ValidationProblem("Chọn một tệp để tải lên.");
        if (request.File.Length > MaxFileSize) return ValidationProblem("Tệp không được vượt quá 25 MB.");
        var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant(); if (!AllowedExtensions.Contains(extension)) return ValidationProblem("Chỉ hỗ trợ PDF, Office hoặc ZIP.");
        await using var stream = request.File.OpenReadStream();
        var result = await resources.UploadAsync(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), new UploadLibraryResourceCommand(request.Title, request.Description ?? string.Empty, request.Category, request.File.FileName, request.File.ContentType ?? "application/octet-stream", request.File.Length), stream, ct);
        return CreatedAtAction(nameof(Download), new { id = result.Id }, result);
    }
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct) { var (resource, stream) = await resources.DownloadAsync(id, ct); return File(stream, resource.ContentType, resource.OriginalFileName, enableRangeProcessing: true); }
}

public sealed class UploadLibraryResourceRequest { [Required, StringLength(300)] public string Title { get; init; } = string.Empty; [StringLength(4000)] public string? Description { get; init; } [Required, StringLength(100)] public string Category { get; init; } = string.Empty; [Required] public IFormFile? File { get; init; } }
