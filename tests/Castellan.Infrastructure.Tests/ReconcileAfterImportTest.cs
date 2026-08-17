using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Castellan.Infrastructure.Data;
using Castellan.Infrastructure.Repositories;
using Castellan.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Tests;

/// <summary>
/// Uzgodnienie samo w sobie (dwie osobne instancje DbContext) nie odtwarza żadnego
/// błędu — sprawdzone osobno. Ten test pilnuje realistycznej sekwencji z tej samej,
/// długo żyjącej instancji: konto odczytane wcześniej w sesji (np. przez ekran Konta),
/// potem import kopii zapasowej (surowe SQL, omija tracker EF), potem uzgodnienie.
/// Bez ChangeTracker.Clear() po SaveChangesAsync/imporcie ta sekwencja rzucała
/// DbUpdateConcurrencyException.
/// </summary>
public class ReconcileAfterImportTest
{
    [Fact]
    public async Task Reconcile_succeeds_after_import_on_the_same_long_lived_context()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_reconcile_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<CastellanDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            using var db = new CastellanDbContext(options);
            db.Database.Migrate();

            var account = Account.Create("ING", AccountKind.Checking, Money.Zero, DateTimeOffset.UtcNow.AddDays(-1));
            db.Accounts.Add(account);
            await db.SaveChangesAsync();

            var accountRepo = new AccountRepository(db);
            var reconcile = new ReconcileAccountUseCase(
                accountRepo, new TransactionRepository(db), new ReconciliationRepository(db), new UnitOfWork(db));

            // Konto trafia do trackera, tak jak wcześniej w sesji zrobiłby to ekran Konta.
            (await accountRepo.GetAsync(account.Id)).Should().NotBeNull();

            var backup = new BackupService(db);
            var exported = await backup.ExportAsync();
            await backup.ImportAsync(exported);

            var act = async () => await reconcile.ExecuteAsync(new ReconcileAccountUseCase.Input(
                account.Id, new Money(17_913), DateTimeOffset.UtcNow.AddMinutes(5)));

            await act.Should().NotThrowAsync();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }
}
