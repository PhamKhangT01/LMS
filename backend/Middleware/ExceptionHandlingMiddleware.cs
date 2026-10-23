using LawLms.Application;
using Microsoft.EntityFrameworkCore;

namespace LawLms.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment environment)
{
    public async Task Invoke(HttpContext context)
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (exception is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
            {
                logger.LogInformation("Request body exceeded its size limit; TraceId: {TraceId}", context.TraceIdentifier);
                await Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Nội dung gửi lên quá lớn", detail: "Nội dung vượt giới hạn cho phép. Hãy rút gọn hoặc bắt đầu cuộc trò chuyện mới.").ExecuteAsync(context);
                return;
            }
            if (exception is NotFoundException or ConflictException or UnauthorizedAppException) logger.LogInformation("Request rejected: {Message}; TraceId: {TraceId}",exception.Message,context.TraceIdentifier);
            else logger.LogError(exception, "Unhandled request error: {TraceId}", context.TraceIdentifier);
            var (status, title) = exception switch { NotFoundException => (StatusCodes.Status404NotFound, "Không tìm thấy tài nguyên"), ConflictException => (StatusCodes.Status409Conflict, "Dữ liệu xung đột"), UnauthorizedAppException => (StatusCodes.Status403Forbidden, "Không có quyền truy cập"), DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Dữ liệu đã thay đổi, vui lòng tải lại"), DbUpdateException => (StatusCodes.Status409Conflict, "Không thể thay đổi dữ liệu đang được sử dụng"), _ => (StatusCodes.Status500InternalServerError, "Lỗi máy chủ") };
            var detail = exception is DbUpdateConcurrencyException ? "Dữ liệu đã thay đổi. Hãy tải lại trước khi xử lý." : exception is DbUpdateException ? "Không thể lưu vì dữ liệu không hợp lệ hoặc xung đột với hồ sơ đã có." : exception is NotFoundException or ConflictException or UnauthorizedAppException || environment.IsDevelopment() ? exception.Message : null;
            await Results.Problem(statusCode: status, title: title, detail: detail, extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
        }
    }
}
