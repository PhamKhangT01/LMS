using LawLms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LawLms.Api;

/// <summary>Used by EF tooling only; runtime secrets stay outside source control.</summary>
public sealed class DesignTimeLmsDbContextFactory : IDesignTimeDbContextFactory<LmsDbContext>
{
    public LmsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__LmsDatabase")
            ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres";
        return new LmsDbContext(new DbContextOptionsBuilder<LmsDbContext>().UseNpgsql(connectionString).Options);
    }
}
