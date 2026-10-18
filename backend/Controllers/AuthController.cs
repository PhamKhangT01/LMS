using LawLms.Application;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace LawLms.Api.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResult>> Login([FromBody] LoginRequest request, CancellationToken ct) => Ok(await authService.LoginAsync(new LoginCommand(request.Email, request.Password), ct));
}

public sealed record LoginRequest([param: Required, EmailAddress] string Email, [param: Required, MinLength(6)] string Password);
