using Castellan.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Tests;

/// <summary>
/// Odpytuje każdą tabelę po migracji. Sam Migrate() + CanConnect() nie wykrywa
/// właściwości modelu, dla której migracja nie utworzyła kolumny — taki rozjazd
/// wychodzi dopiero jako "no such column" w działającej aplikacji.
/// </summary>
public class SchemaMatchesModelTest
{
    [Fact]
    public void Every_entity_can_be_queried_after_migration()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_schema_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<CastellanDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            using (var db = new CastellanDbContext(options))
            {
                db.Database.Migrate();

                // Każde zapytanie materializuje wszystkie kolumny encji — brak
                // którejkolwiek w bazie rzuci "no such column" i wywali test.
                var queries = new Action[]
                {
                    () => db.Accounts.ToList(),
                    () => db.Categories.ToList(),
                    () => db.Transactions.ToList(),
                    () => db.MonthBudgets.Include(b => b.Envelopes).ToList(),
                    () => db.Reconciliations.ToList(),
                    () => db.RawNotifications.ToList(),
                    () => db.CategoryRules.ToList(),
                    () => db.Funds.ToList(),
                    () => db.Assets.ToList(),
                };

                foreach (var query in queries)
                    query.Should().NotThrow();
            }

        }
        finally
        {
            SqliteConnection.ClearAllPools();
            // Sprzątanie nie może przesłonić wyniku testu — Windows potrafi
            // trzymać plik WAL jeszcze chwilę po zamknięciu połączenia.
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }
}
