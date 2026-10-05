using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Infrastructure;
using Castellan.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Castellan.Infrastructure.Tests;

/// <summary>
/// Kategorie sieje się dwiema drogami i łatwo dodać nową tylko do jednej z nich.
/// Pierwszy zasiew dotyczy pustej bazy; lista `EnsureCategory` dosiewa do baz, które
/// już istnieją. Pominięcie tej drugiej jest NIEWIDOCZNE u kogoś, kto instaluje
/// aplikację od zera — a właśnie tak wygląda każdy test na świeżej bazie.
/// </summary>
public class CategorySeedingTests
{
    private static ServiceProvider BuildProvider(string dbPath) =>
        new ServiceCollection().AddInfrastructure(dbPath).BuildServiceProvider();

    [Fact]
    public void A_fresh_install_gets_the_category()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_seed_{Guid.NewGuid():N}.db");
        try
        {
            var provider = BuildProvider(dbPath);
            provider.ApplyMigrations();
            provider.SeedDefaultData();

            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CastellanDbContext>();

            db.Categories.Should().Contain(c => c.Name == "Kosmetyczka" && c.Kind == CategoryKind.Expense);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public void An_existing_database_gets_the_category_on_the_next_start()
    {
        // Baza sprzed dodania kategorii: pelna innych kategorii, ale bez tej jednej.
        // To jest ten przypadek, ktory pierwszy zasiew omija szerokim lukiem, bo
        // wchodzi tylko wtedy, gdy kategorii nie ma ZADNYCH.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_seed_{Guid.NewGuid():N}.db");
        try
        {
            var provider = BuildProvider(dbPath);
            provider.ApplyMigrations();
            provider.SeedDefaultData();

            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CastellanDbContext>();
                db.Categories.RemoveRange(db.Categories.Where(c => c.Name == "Kosmetyczka"));
                db.SaveChanges();
                db.Categories.Should().NotContain(c => c.Name == "Kosmetyczka");
            }

            provider.SeedDefaultData();

            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CastellanDbContext>();
                db.Categories.Should().Contain(c => c.Name == "Kosmetyczka");
            }
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public void An_archived_category_does_not_come_back()
    {
        // Zarchiwizowana kategoria zachowuje nazwe, wiec dosiew jej nie odtwarza.
        // Bez tego kazde uruchomienie aplikacji cofaloby decyzje uzytkownika.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_seed_{Guid.NewGuid():N}.db");
        try
        {
            var provider = BuildProvider(dbPath);
            provider.ApplyMigrations();
            provider.SeedDefaultData();

            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CastellanDbContext>();
                db.Categories.Single(c => c.Name == "Kosmetyczka").Archive();
                db.SaveChanges();
            }

            provider.SeedDefaultData();

            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CastellanDbContext>();
                db.Categories.Where(c => c.Name == "Kosmetyczka").Should().ContainSingle()
                    .Which.IsArchived.Should().BeTrue();
            }
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public void A_renamed_category_keeps_its_identity_so_history_survives()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_seed_{Guid.NewGuid():N}.db");
        try
        {
            var provider = BuildProvider(dbPath);
            provider.ApplyMigrations();
            provider.SeedDefaultData();

            CategoryId id;
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CastellanDbContext>();

                // Baza sprzed zmiany nazwy: dokladnie to, co ma u siebie ktos, kto
                // uzywa aplikacji od dawna.
                var partner = db.Categories.Single(c => c.Name == "Wpłata partnera");
                id = partner.Id;
                partner.Rename("Wpłata małżonka");
                db.SaveChanges();
            }

            provider.SeedDefaultData();

            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CastellanDbContext>();

                // Zmiana nazwy, a NIE nowa kategoria: transakcje i plany wiaza sie po ID,
                // wiec dodanie nowej zostawiloby historie przy starej, osieroconej nazwie.
                db.Categories.Should().NotContain(c => c.Name == "Wpłata małżonka");
                db.Categories.Single(c => c.Name == "Wpłata partnera").Id.Should().Be(id);
            }
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
