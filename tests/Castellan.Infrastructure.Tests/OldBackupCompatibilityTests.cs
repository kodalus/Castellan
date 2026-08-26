using System.Text.Json;
using Castellan.Application.Dto;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Castellan.Infrastructure.Data;
using Castellan.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Tests;

/// <summary>
/// Kopia zapasowa zrobiona przed usunięciem pola musi się dalej wczytywać. Plik JSON
/// leży u użytkownika na Dysku albo w mailu i nikt go nie zaktualizuje — import, który
/// wywraca się na nadmiarowym polu, zamienia kopię zapasową w bezużyteczny plik
/// dokładnie wtedy, gdy jest najbardziej potrzebna.
/// </summary>
public class OldBackupCompatibilityTests
{
    /// <summary>Kopia w formacie sprzed usunięcia kolumny — z polem LiquidityTier.</summary>
    private const string OldFormat = """
    {
      "Version": 1,
      "ExportedAt": "2026-08-20T10:00:00.0000000+00:00",
      "Accounts": [
        {
          "Id": "01a03824-0000-7000-8000-000000000001",
          "Name": "ING",
          "Kind": 0,
          "LiquidityTier": 2,
          "BankKey": "ING",
          "IsArchived": false,
          "LastReconciledBalance": 250000,
          "LastReconciledAt": "2026-08-01T00:00:00.0000000+00:00"
        }
      ]
    }
    """;

    [Fact]
    public async Task A_backup_that_still_carries_the_removed_field_imports_fine()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_oldbak_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<CastellanDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            var db = new CastellanDbContext(options);
            db.Database.Migrate();

            // Coś, co import ma zastąpić — inaczej test przeszedłby także wtedy,
            // gdyby import po cichu nic nie zrobił.
            db.Accounts.Add(Account.Create("Do zastąpienia", AccountKind.Savings, new Money(999),
                DateTimeOffset.UtcNow.AddYears(-1)));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var data = JsonSerializer.Deserialize<CastellanExport>(OldFormat)!;
            await new BackupService(db).ImportAsync(data);
            db.ChangeTracker.Clear();

            var account = await db.Accounts.SingleAsync();
            account.Name.Should().Be("ING");
            account.BankKey.Should().Be("ING");
            account.LastReconciledBalance.Grosze.Should().Be(250_000,
                "kolumny po usuniętej muszą trafić na swoje miejsca, a nie przesunąć się o jedną");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(p); } catch (IOException) { }
        }
    }
}
