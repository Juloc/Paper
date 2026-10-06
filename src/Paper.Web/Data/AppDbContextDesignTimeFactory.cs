using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Paper.Web.Data;

public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var password = Environment.GetEnvironmentVariable("PAPER_POSTGRES_PASSWORD") ?? "change-me";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql($"Host=localhost;Port=5432;Database=paper;Username=paper;Password={password}")
            .Options;
        return new AppDbContext(options);
    }
}
