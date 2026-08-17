using Castellan.Application;
using Castellan.Application.Parsers;
using Castellan.Application.Repositories;
using Castellan.Application.Services;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Infrastructure.Data;
using Castellan.Infrastructure.Parsers;
using Castellan.Infrastructure.Repositories;
using Castellan.Infrastructure.Services;
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
        services.AddScoped<IFundRepository, FundRepository>();
        services.AddScoped<IAssetRepository, AssetRepository>();

        services.AddScoped<IBackupService, BackupService>();

        services.AddSingleton<INotificationParser, IngNotificationParser>();
        services.AddSingleton<INotificationParser, RevolutNotificationParser>();
        services.AddSingleton<INotificationParser, GoogleWalletNotificationParser>();

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

        if (!db.Categories.Any(c => !c.IsSystem))
        {
            var expenses = new[]
            {
                "Produkty do domu", "Restauracje i kawiarnie", "Transport", "Paliwo",
                "Mieszkanie i czynsz", "Media i rachunki", "Zdrowie i apteka",
                "Rozrywka", "Ubrania i obuwie", "Elektronika", "Edukacja",
                "Sport i rekreacja", "Higiena i kosmetyki", "Podróże", "Inne wydatki",
                "Inwestycje", "Dobroczynność", "Dzieci", "Przedszkole", "Rezerwy",
            };
            var incomes = new[]
            {
                "Wypłata", "Wpłata małżonka", "800+", "Inne",
            };

            foreach (var name in expenses)
                db.Categories.Add(Category.Create(name, CategoryKind.Expense));
            foreach (var name in incomes)
                db.Categories.Add(Category.Create(name, CategoryKind.Income));

            db.SaveChanges();
            return;
        }

        // Kategorie dodane po pierwszym seedzie — dopilnuj ich także w istniejących bazach.
        // Archiwizacja kategorii zachowuje nazwę, więc zarchiwizowane nie wracają.
        EnsureCategory(db, "Inwestycje", CategoryKind.Expense);
        EnsureCategory(db, "Dobroczynność", CategoryKind.Expense);
        EnsureCategory(db, "Dzieci", CategoryKind.Expense);
        EnsureCategory(db, "Przedszkole", CategoryKind.Expense);
        EnsureCategory(db, "Rezerwy", CategoryKind.Expense);
        EnsureCategory(db, "800+", CategoryKind.Income);
        EnsureCategory(db, "Wpłata małżonka", CategoryKind.Income);

        // Jeden paragon ze sklepu to zwykle jedzenie + chemia + higiena naraz,
        // więc "Jedzenie" zmieniło się w szerszą kategorię zakupową.
        RenameCategory(db, from: "Jedzenie", to: "Produkty do domu");

        // Nazwy przychodów skrócone do tych realnie używanych. Zmiana nazwy (a nie
        // dodanie nowej kategorii) zachowuje powiązane transakcje i reguły — te
        // wiążą się po ID, nie po nazwie.
        RenameCategory(db, from: "Wynagrodzenie", to: "Wypłata");
        RenameCategory(db, from: "Inne przychody", to: "Inne");

        db.SaveChanges();
    }

    private static void EnsureCategory(CastellanDbContext db, string name, CategoryKind kind)
    {
        if (!db.Categories.Any(c => !c.IsSystem && c.Name == name))
            db.Categories.Add(Category.Create(name, kind));
    }

    /// <summary>
    /// Zmienia nazwę tylko wtedy, gdy stara nazwa nadal istnieje, a nowej jeszcze nie ma —
    /// dzięki temu nie nadpisze własnych zmian użytkownika ani nie zrobi duplikatu.
    /// </summary>
    private static void RenameCategory(CastellanDbContext db, string from, string to)
    {
        if (db.Categories.Any(c => !c.IsSystem && c.Name == to)) return;

        var existing = db.Categories.FirstOrDefault(c => !c.IsSystem && c.Name == from);
        existing?.Rename(to);
    }
}
