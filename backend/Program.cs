using System.Threading.RateLimiting;
using System.Security.Claims;
using LawLms.Api.Middleware;
using LawLms.Application;
using LawLms.Infrastructure.Persistence;
using LawLms.Infrastructure.Repositories;
using LawLms.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();
var connectionString = builder.Configuration.GetConnectionString("LmsDatabase") ?? throw new InvalidOperationException("ConnectionStrings:LmsDatabase phải được cấu hình.");
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? throw new InvalidOperationException("Cấu hình Jwt không hợp lệ.");
if (string.IsNullOrWhiteSpace(jwtOptions.Key) || jwtOptions.Key.Length < 32) throw new InvalidOperationException("Jwt:Key phải có ít nhất 32 ký tự và được cấu hình qua secret/biến môi trường.");
builder.Services.AddDbContext<LmsDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.AddScoped<IUserRepository, UserRepository>(); builder.Services.AddScoped<ICourseRepository, CourseRepository>(); builder.Services.AddScoped<IEnrollmentRepository, EnrollmentRepository>(); builder.Services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<LmsDbContext>());
builder.Services.AddScoped<ITeachingClassRepository, TeachingClassRepository>();
builder.Services.AddScoped<ILibraryResourceRepository, LibraryResourceRepository>();
builder.Services.AddScoped<IQuizRepository, QuizRepository>();
builder.Services.AddScoped<IPasswordService, PasswordService>(); builder.Services.AddScoped<ITokenService, JwtTokenService>(); builder.Services.AddScoped<IAuthService, AuthService>(); builder.Services.AddScoped<ICourseService, CourseService>();
builder.Services.AddScoped<ITeachingClassService, TeachingClassService>();
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>(); builder.Services.AddScoped<ILibraryResourceService, LibraryResourceService>();
builder.Services.AddScoped<IQuizService, QuizService>();
builder.Services.AddScoped<IOperationsService, OperationsService>();
builder.Services.AddScoped<IQuestionBankService, QuestionBankService>();
builder.Services.AddScoped<IAcademicReportService, AcademicReportService>();
builder.Services.AddHostedService<LawLms.Api.AttemptExpiryWorker>();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("frontend", policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidIssuer = jwtOptions.Issuer, ValidateAudience = true, ValidAudience = jwtOptions.Audience, ValidateLifetime = true, ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)), ClockSkew = TimeSpan.FromMinutes(1) });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous", _ => new FixedWindowRateLimiterOptions { PermitLimit = 240, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "anonymous", _ => new FixedWindowRateLimiterOptions { PermitLimit = 100, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();
if (args.Contains("--seed-test-data"))
{
    var target = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
    if (!app.Environment.IsDevelopment() || target.Host is not ("localhost" or "127.0.0.1" or "::1"))
        throw new InvalidOperationException("Bộ dữ liệu kiểm thử chỉ chạy trong Development với PostgreSQL local.");
    var seedPassword = builder.Configuration["Testing:SeedPassword"];
    if (string.IsNullOrWhiteSpace(seedPassword) || seedPassword.Length < 12)
        throw new InvalidOperationException("Đặt Testing__SeedPassword tối thiểu 12 ký tự trước khi seed.");
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
    var storage = Path.GetFullPath(builder.Configuration["Storage:RootPath"] ?? "uploads");
    var result = await new TestDataSeeder(db, storage, seedPassword).SeedAsync();
    Console.WriteLine("TEST_SEED_REPORT=" + System.Text.Json.JsonSerializer.Serialize(result));
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors("frontend");
app.UseAuthentication();
app.UseRateLimiter();
app.UseMiddleware<AccountAccessMiddleware>();
app.UseAuthorization();
app.MapControllers().RequireRateLimiting("api");
app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapGet("/health/ready", async (LmsDbContext db, CancellationToken ct) => await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(StatusCodes.Status503ServiceUnavailable)).AllowAnonymous();
app.MapGet("/health", async (LmsDbContext db, CancellationToken ct) => await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "healthy" }) : Results.StatusCode(StatusCodes.Status503ServiceUnavailable)).AllowAnonymous();
if (builder.Configuration.GetValue<bool>("Database:SeedDemoData")) { using var scope = app.Services.CreateScope(); await DbSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<LmsDbContext>()); }
// Serve React routes while preserving real 404 responses for API/health paths.
app.MapFallback(async context =>
{
    if (Path.HasExtension(context.Request.Path.Value) || context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/health") ||
        (context.Request.Method != "GET" && context.Request.Method != "HEAD"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    var index = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html");
    if (!File.Exists(index)) { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    context.Response.ContentType = "text/html; charset=utf-8";
    context.Response.Headers.CacheControl = "no-cache";
    await context.Response.SendFileAsync(index);
});
app.Run();
