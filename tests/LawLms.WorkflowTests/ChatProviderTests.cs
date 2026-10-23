using System.Net;
using System.Text;
using System.Text.Json;
using LawLms.Application;
using LawLms.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Xunit;

public class ChatProviderTests
{
    private static ChatCompletionRequest Request() => new("guide", "Đổi mật khẩu thế nào?", "Student", [new("user", "Tôi đang dùng LMS"), new("assistant", "Bạn cần trợ giúp gì?")], [new(new("guide:password", "Đổi mật khẩu", "guide", View: "password"), "Mở Đổi mật khẩu.")]);
    private static string ValidBody(string answer = "Mở Đổi mật khẩu.") => JsonSerializer.Serialize(new { status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = JsonSerializer.Serialize(new { answer, sourceIds = new[] { "guide:password" } }) } } } } });
    private static OpenAiChatCompletionProvider Provider(Handler handler, string key = "test-provider-key") => new(new HttpClient(handler), Options.Create(new OpenAiChatOptions { ApiKey = key }));
    private static HttpResponseMessage JsonResponse(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact] public async Task PayloadUsesResponsesStructuredOutputWithoutStorageOrTools()
    {
        string? json = null; Uri? uri = null; string? scheme = null;
        var handler = new Handler(async (request, ct) => { uri = request.RequestUri; scheme = request.Headers.Authorization?.Scheme; json = await request.Content!.ReadAsStringAsync(ct); return JsonResponse(ValidBody()); });
        var result = await Provider(handler).CompleteAsync(Request(), default);
        Assert.Equal("Mở Đổi mật khẩu.", result.Answer); Assert.Equal(["guide:password"], result.SourceIds);
        Assert.Equal("https://api.openai.com/v1/responses", uri!.ToString()); Assert.Equal("Bearer", scheme);
        using var body = JsonDocument.Parse(json!); var root = body.RootElement;
        Assert.False(root.GetProperty("store").GetBoolean()); Assert.False(root.TryGetProperty("tools", out _));
        Assert.Equal("gpt-4.1-mini", root.GetProperty("model").GetString());
        var format = root.GetProperty("text").GetProperty("format"); Assert.Equal("json_schema", format.GetProperty("type").GetString()); Assert.True(format.GetProperty("strict").GetBoolean());
        Assert.False(format.GetProperty("schema").GetProperty("additionalProperties").GetBoolean());
        var input = root.GetProperty("input"); Assert.Equal("developer", input[0].GetProperty("role").GetString());
        Assert.Contains("guide:password", input[0].GetProperty("content").GetString()); Assert.Equal("user", input[1].GetProperty("role").GetString()); Assert.Equal("assistant", input[2].GetProperty("role").GetString());
        Assert.DoesNotContain("test-provider-key", json!);
    }
    [Fact] public async Task MissingKeyDoesNotSendNetworkRequest()
    {
        var called = false; var provider = Provider(new Handler((_, _) => { called = true; return Task.FromResult(JsonResponse(ValidBody())); }), "");
        Assert.False(provider.Available); var exception = await Assert.ThrowsAsync<ChatProviderException>(() => provider.CompleteAsync(Request(), default)); Assert.Equal(503, exception.StatusCode); Assert.False(called);
    }
    [Theory] [InlineData(429, 429)] [InlineData(401, 503)] [InlineData(403, 503)] [InlineData(500, 503)] [InlineData(503, 503)] [InlineData(400, 502)]
    public async Task HttpErrorsAreSanitized(int status, int expected)
    {
        var provider = Provider(new Handler((_, _) => Task.FromResult(JsonResponse("private-provider-error sk-secret", (HttpStatusCode)status))));
        var error = await Assert.ThrowsAsync<ChatProviderException>(() => provider.CompleteAsync(Request(), default)); Assert.Equal(expected, error.StatusCode); Assert.DoesNotContain("private-provider", error.Message); Assert.DoesNotContain("sk-secret", error.Message);
    }
    [Theory]
    [InlineData("{\"status\":\"incomplete\",\"output\":[]}")]
    [InlineData("{\"status\":\"completed\",\"output_text\":\"wrong root shortcut\",\"output\":[]}")]
    [InlineData("{\"status\":\"completed\",\"output\":[{\"content\":[{\"type\":\"refusal\",\"refusal\":\"private reason\"}]}]}")]
    [InlineData("{\"status\":\"failed\",\"error\":{\"message\":\"private reason\"}}")]
    [InlineData("not-json private reason")]
    [InlineData("[]")]
    public async Task IncompleteRefusedOrMalformedResponsesFailSafely(string body)
    {
        var provider = Provider(new Handler((_, _) => Task.FromResult(JsonResponse(body))));
        var error = await Assert.ThrowsAsync<ChatProviderException>(() => provider.CompleteAsync(Request(), default)); Assert.Equal(502, error.StatusCode); Assert.DoesNotContain("private reason", error.Message);
    }
    [Theory]
    [InlineData("{\"answer\":\"\",\"sourceIds\":[]}")]
    [InlineData("{\"answer\":\"answer\",\"sourceIds\":[123]}")]
    [InlineData("{\"answer\":\"answer\",\"sourceIds\":\"guide:password\"}")]
    [InlineData("{\"answer\":123,\"sourceIds\":[]}")]
    public async Task StructuredAnswerShapeIsValidated(string text)
    {
        var body = JsonSerializer.Serialize(new { status = "completed", output = new[] { new { content = new[] { new { type = "output_text", text } } } } });
        var error = await Assert.ThrowsAsync<ChatProviderException>(() => Provider(new Handler((_, _) => Task.FromResult(JsonResponse(body)))).CompleteAsync(Request(), default)); Assert.Equal(502, error.StatusCode);
    }
    [Fact] public async Task ProviderTimeoutIsUnavailableAndUserCancellationPropagates()
    {
        var provider = Provider(new Handler((_, _) => throw new TaskCanceledException()));
        var error = await Assert.ThrowsAsync<ChatProviderException>(() => provider.CompleteAsync(Request(), default)); Assert.Equal(503, error.StatusCode);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.CompleteAsync(Request(), cancellation.Token));
    }
    [Fact] public async Task NetworkFailureDoesNotExposeProviderDetails()
    {
        var error = await Assert.ThrowsAsync<ChatProviderException>(() => Provider(new Handler((_, _) => throw new HttpRequestException("private network detail"))).CompleteAsync(Request(), default));
        Assert.Equal(503, error.StatusCode); Assert.DoesNotContain("private network", error.Message);
    }
    [Fact] public async Task LargeProviderResponseIsRejected()
    {
        var provider = Provider(new Handler((_, _) => Task.FromResult(JsonResponse(new string('x', 1024 * 1024 + 1)))));
        var error = await Assert.ThrowsAsync<ChatProviderException>(() => provider.CompleteAsync(Request(), default)); Assert.Equal(502, error.StatusCode);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request, cancellationToken);
    }
}
