using LawLms.Application;
using LawLms.Domain;
using Xunit;

public class LibraryResourceTests
{
    [Fact]
    public async Task MultipleResourcesKeepUploaderNamesWithoutConcurrentContextQueries()
    {
        var first = new AppUser { FullName = "Giảng viên A" };
        var second = new AppUser { FullName = "Giảng viên B" };
        IReadOnlyList<LibraryResource> rows = [
            new() { Title = "Bài giảng", UploadedById = first.Id },
            new() { Title = "Đề cương", UploadedById = second.Id },
            new() { Title = "Tài liệu tham khảo", UploadedById = first.Id },
        ];
        var reading = 0;
        async Task<IReadOnlyList<AppUser>> Read(IReadOnlyCollection<Guid> ids)
        {
            if (Interlocked.Exchange(ref reading, 1) != 0)
                throw new InvalidOperationException("A shared DbContext cannot run overlapping queries.");
            try
            {
                await Task.Delay(20);
                return new[] { first, second }.Where(x => ids.Contains(x.Id)).ToList();
            }
            finally { Interlocked.Exchange(ref reading, 0); }
        }
        var service = new LibraryResourceService(
            Stub<ILibraryResourceRepository>.Create((method, _) => method == "SearchAsync" ? Task.FromResult(rows) : throw new NotImplementedException(method)),
            Stub<IUserRepository>.Create((method, args) => method == "GetByIdsAsync" ? Read((IReadOnlyCollection<Guid>)args![0]!) : throw new NotImplementedException(method)),
            Stub<IFileStorage>.Create((method, _) => throw new NotImplementedException(method)),
            Stub<IUnitOfWork>.Create((method, _) => throw new NotImplementedException(method)));
        var result = await service.SearchAsync(null, default);
        Assert.Equal(rows.Select(x => x.Id), result.Select(x => x.Id));
        Assert.Equal(new[] { "Giảng viên A", "Giảng viên B", "Giảng viên A" }, result.Select(x => x.UploadedBy));
    }
}
