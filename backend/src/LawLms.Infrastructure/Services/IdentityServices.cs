using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LawLms.Application;
using LawLms.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LawLms.Infrastructure.Services;

public sealed class JwtOptions { public const string SectionName = "Jwt"; public string Issuer { get; init; } = string.Empty; public string Audience { get; init; } = string.Empty; public string Key { get; init; } = string.Empty; public int AccessTokenMinutes { get; init; } = 60; }
public sealed class PasswordService : IPasswordService { public bool Verify(AppUser user, string password) => new PasswordHasher<AppUser>().VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed; }
public sealed class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    public string Create(AppUser user) { var o = options.Value; var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.FullName), new Claim(ClaimTypes.Role, user.Role.ToString()), new Claim("version", user.TokenVersion.ToString()) }; return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(o.Issuer, o.Audience, claims, expires: DateTime.UtcNow.AddMinutes(o.AccessTokenMinutes), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key)), SecurityAlgorithms.HmacSha256))); }
}
