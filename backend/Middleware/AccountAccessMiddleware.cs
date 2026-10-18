using System.Security.Claims;
using LawLms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LawLms.Api.Middleware;

public sealed class AccountAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context,LmsDbContext db)
    {
        if(context.User.Identity?.IsAuthenticated==true)
        {
            var id=Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user=await db.Users.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,context.RequestAborted);
            if(user is null || !user.IsActive || context.User.FindFirstValue("version")!=user.TokenVersion.ToString())
            {await Results.Problem(statusCode:401,detail:"Phiên đăng nhập hết hiệu lực. Hãy đăng nhập lại.").ExecuteAsync(context);return;}
            if(user.MustChangePassword && context.Request.Path!="/api/operations/change-password")
            {await Results.Problem(statusCode:403,detail:"Bạn cần đổi mật khẩu trước khi sử dụng hệ thống.").ExecuteAsync(context);return;}
        }
        await next(context);
    }
}
