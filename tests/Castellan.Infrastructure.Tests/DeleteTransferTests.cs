using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Castellan.Infrastructure.Data;
using Castellan.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Tests;

/// <summary>
/// Przelew to para wpisów, więc usunięcie jednej strony kasuje obie — inaczej saldo
/// jednego konta zmieniłoby się bez odpowiednika na drugim. Ekran musi jednak wiedzieć,
/// że zniknęły DWA wiersze: gdy usuwał ze swojej listy tylko dotknięty, druga noga
/// zostawała widoczna, a próba jej usunięcia kończyła się wyjątkiem o transakcji,
/// której już nie było.
/// </summary>
public class DeleteTransferTests
{
    private static async Task<(CastellanDbContext db, DeleteTransactionUseCase delete, Transaction outgoing, Transaction incoming)>
        SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        var now = DateTimeOffset.UtcNow.AddYears(-1);
        var checking = Account.Create("ING", AccountKind.Checking, new Money(100_000), now);
        var savings = Account.Create("OKO ING", AccountKind.Savings, Money.Zero, now);
        db.Accounts.AddRange(checking, savings);
        db.Categories.Add(Category.Create("Produkty do domu", CategoryKind.Expense));
        await db.SaveChangesAsync();

        // Przelew testowy na 1 zł, dokładnie jak zgłoszony z eksploatacji.
        var groupId = Guid.NewGuid();
        var outgoing = Transaction.CreateManual(checking.Id, new Money(-100), DateTimeOffset.Now, Category.UnsortedId);
        var incoming = Transaction.CreateManual(savings.Id, new Money(100), DateTimeOffset.Now, Category.UnsortedId);
        outgoing.SetTransferGroup(groupId);
        incoming.SetTransferGroup(groupId);
        db.Transactions.AddRange(outgoing, incoming);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return (db, new DeleteTransactionUseCase(new TransactionRepository(db), new UnitOfWork(db)), outgoing, incoming);
    }

    [Fact]
    public async Task Deleting_one_leg_reports_both_legs_as_gone()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_deltr_{Guid.NewGuid():N}.db");
        try
        {
            var (db, delete, outgoing, incoming) = await SetupAsync(dbPath);

            var removed = await delete.ExecuteAsync(incoming.Id);
            db.ChangeTracker.Clear();

            removed.Should().BeEquivalentTo([incoming.Id, outgoing.Id],
                "ekran musi wiedzieć o obu wierszach, nie tylko o dotkniętym");
            (await db.Transactions.CountAsync()).Should().Be(0);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Deleting_the_second_leg_afterwards_is_a_no_op_not_an_error()
    {
        // Dokładnie zgłoszony przebieg: najpierw dodatnia, potem ujemna. Druga próba
        // trafiała w transakcję skasowaną razem z pierwszą i wywalała wyjątek.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_deltr_{Guid.NewGuid():N}.db");
        try
        {
            var (db, delete, outgoing, incoming) = await SetupAsync(dbPath);

            await delete.ExecuteAsync(incoming.Id);
            db.ChangeTracker.Clear();

            var act = async () => await delete.ExecuteAsync(outgoing.Id);
            await act.Should().NotThrowAsync();
            (await delete.ExecuteAsync(outgoing.Id)).Should().BeEmpty();
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Deleting_an_ordinary_transaction_touches_only_itself()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_deltr_{Guid.NewGuid():N}.db");
        try
        {
            var (db, delete, _, _) = await SetupAsync(dbPath);

            var account = await db.Accounts.FirstAsync(a => a.Name == "ING");
            var category = await db.Categories.FirstAsync(c => c.Name == "Produkty do domu");
            var solo = Transaction.CreateManual(account.Id, new Money(-4_500), DateTimeOffset.Now, category.Id);
            db.Transactions.Add(solo);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var removed = await delete.ExecuteAsync(solo.Id);
            db.ChangeTracker.Clear();

            removed.Should().ContainSingle().Which.Should().Be(solo.Id);
            (await db.Transactions.CountAsync()).Should().Be(2, "obie nogi przelewu zostają nietknięte");
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
