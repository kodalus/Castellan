using Castellan.Application;
using Castellan.Application.Repositories;
using Castellan.Infrastructure.Data;
using Castellan.Infrastructure.Repositories;
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

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IMonthBudgetRepository, MonthBudgetRepository>();
        services.AddScoped<IReconciliationRepository, ReconciliationRepository>();

        return services;
    }

    public static void ApplyMigrations(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<CastellanDbContext>().Database.Migrate();
    }
}
