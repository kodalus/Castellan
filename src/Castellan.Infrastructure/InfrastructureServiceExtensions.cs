using Castellan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Castellan.Infrastructure;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string dbPath)
    {
        services.AddDbContext<CastellanDbContext>(opts =>
            opts.UseSqlite($"Data Source={dbPath}")
                .AddInterceptors(new SqlitePragmaInterceptor()));

        return services;
    }

    public static void ApplyMigrations(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<CastellanDbContext>().Database.Migrate();
    }
}
