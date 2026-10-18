using LawLms.Application;
using Microsoft.Extensions.Options;

namespace LawLms.Infrastructure.Services;

public sealed class StorageOptions { public const string SectionName = "Storage"; public string RootPath { get; init; } = "uploads"; }
public sealed class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    private readonly string _rootPath = Path.GetFullPath(options.Value.RootPath);
    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct) { Directory.CreateDirectory(_rootPath); var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}"; await using var target = File.Create(Path.Combine(_rootPath, fileName)); await content.CopyToAsync(target, ct); return fileName; }
    public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken ct) { var safeFileName = Path.GetFileName(storedFileName); var path = Path.Combine(_rootPath, safeFileName); Stream? stream = File.Exists(path) ? File.OpenRead(path) : null; return Task.FromResult(stream); }
    public Task DeleteAsync(string storedFileName, CancellationToken ct) { var safeFileName = Path.GetFileName(storedFileName); var path = Path.Combine(_rootPath, safeFileName); if (File.Exists(path)) File.Delete(path); return Task.CompletedTask; }
}
