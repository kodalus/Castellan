using Castellan.Application;
using Castellan.Application.Parsers;
using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Infrastructure.Data;
using Castellan.Infrastructure.Parsers;
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
        services.AddScoped<IRawNotificationRepository, RawNotificationRepository>();
        services.AddScoped<ICategoryRuleRepository, CategoryRuleRepository>();

        services.AddSingleton<INotificationParser, IngNotificationParser>();
        services.AddSingleton<INotificationParser, RevolutNotificationParser>();

        return services;
    }

    public static void ApplyMigrations(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<CastellanDbContext>().Database.Migrate();
    }

    public static void SeedDefaultData(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CastellanDbContext>();

        if (db.Categories.Any(c => !c.IsSystem)) return;

        var expenses = new[]
        {
            "Jedzenie", "Restauracje i kawiarnie", "Transport", "Paliwo",
            "Mieszkanie i czynsz", "Media i rachunki", "Zdrowie i apteka",
            "Rozrywka", "Ubrania i obuwie", "Elektronika", "Edukacja",
            "Sport i rekreacja", "Higiena i kosmetyki", "Podróże", "Inne wydatki",
        };
        var incomes = new[]
        {
            "Wynagrodzenie", "Premia", "Zwrot kosztów", "Inne przychody",
        };

        foreach (var name in expenses)
            db.Categories.Add(Category.Create(name, CategoryKind.Expense));
        foreach (var name in incomes)
            db.Categories.Add(Category.Create(name, CategoryKind.Income));

        db.SaveChanges();
    }
}
