using System.Security.Claims;
using LawLms.Application;
using LawLms.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LawLms.Api.Controllers;

[ApiController, Authorize, Route("api/chat")]
public sealed class ChatController(IChatService chat) : ControllerBase
{
    private Actor Actor => new(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), Enum.Parse<UserRole>(User.FindFirstValue(ClaimTypes.Role)!));

    [HttpGet("context")]
    public async Task<ActionResult<ChatContext>> Context(CancellationToken ct) => Ok(await chat.ContextAsync(Actor, ct));

    [HttpPost("messages"), EnableRateLimiting("chat"), RequestSizeLimit(100 * 1024)]
    public async Task<ActionResult<ChatReply>> Send(ChatMessageRequest request, CancellationToken ct)
    {
        try { return Ok(await chat.SendAsync(request, Actor, ct)); }
        catch (ChatInputException exception) { return Problem(statusCode: 400, title: "Câu hỏi không hợp lệ", detail: exception.Message); }
        catch (ChatProviderException exception) { return Problem(statusCode: exception.StatusCode, title: "Chatbot tạm thời chưa sẵn sàng", detail: exception.Message); }
    }
}
