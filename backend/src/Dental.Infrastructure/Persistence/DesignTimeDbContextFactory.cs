using Dental.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dental.Infrastructure.Persistence;

/// <summary>Для dotnet ef migrations: контекст без хоста приложения (подключение роли-владельца).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("DENTAL_OWNER_CS")
                 ?? "Host=localhost;Port=5432;Database=dental;Username=dental_owner;Password=dental_owner";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(cs, o => o.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options, new TenantContext());
    }
}
