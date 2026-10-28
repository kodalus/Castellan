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
/// Przelew WYKRYTY z powiadomień trzyma parę pod innym polem niż przelew potwierdzony:
/// ProposedTransferGroupId zamiast TransferGroupId. Usuwanie znało tylko to drugie,
/// więc kasowało samą dotkniętą nogę — druga zostawała na swoim koncie i saldo nie
/// wracało do stanu sprzed przelewu, mimo że przelew z listy „zniknął".
/// </summary>
public class DeleteProposedTransferTests
{
    private const long Saldo = 500_000;
    private const long Kwota = 120_000;

    [Fact]
    public async Task Deleting_both_sides_brings_both_balances_back()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, z, na) = await SetupAsync(dbPath);
            var (wychodzaca, _) = await ProposeAsync(db, z, na);

            await Delete(db).ExecuteAsync(wychodzaca, removeProposedPair: true);

            db.ChangeTracker.Clear();
            (await db.Transactions.CountAsync()).Should().Be(0);

            var salda = await Balances(db);
            salda.Single(a => a.Id == z).CurrentBalance.Grosze.Should().Be(Saldo);
            salda.Single(a => a.Id == na).CurrentBalance.Grosze.Should().Be(Saldo);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Deleting_only_one_side_leaves_the_other_as_an_ordinary_entry()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, z, na) = await SetupAsync(dbPath);
            var (wychodzaca, _) = await ProposeAsync(db, z, na);

            // Para mogla byc przypadkowym zestawieniem dwoch niezwiazanych kwot —
            // wtedy druga strona jest prawdziwa transakcja i nie wolno jej ruszac.
            await Delete(db).ExecuteAsync(wychodzaca, removeProposedPair: false);

            db.ChangeTracker.Clear();
            var zostala = await db.Transactions.SingleAsync();
            zostala.AccountId.Should().Be(na);

            // I nie moze zostac z propozycja wskazujaca na grupe bez pary: takiej
            // propozycji nie da sie potwierdzic, a wpis utknalby z napisem
            // "potwierdz w Skrzynce" na zawsze.
            zostala.ProposedTransferGroupId.Should().BeNull();
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_confirmed_transfer_still_goes_as_a_pair()
    {
        var dbPath = NewDbPath();
        try
        {
            var (db, z, na) = await SetupAsync(dbPath);
            var (wychodzaca, groupId) = await ProposeAsync(db, z, na);

            await new ConfirmTransferUseCase(
                new TransactionRepository(db), new CategoryRepository(db),
                new FundRepository(db), new UnitOfWork(db)).ExecuteAsync(groupId);
            db.ChangeTracker.Clear();

            // Przy potwierdzonym przelewie nie ma o co pytac — obie strony znikaja
            // razem, tak jak dzialalo to do tej pory.
            await Delete(db).ExecuteAsync(wychodzaca, removeProposedPair: false);

            db.ChangeTracker.Clear();
            (await db.Transactions.CountAsync()).Should().Be(0);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static async Task<(TransactionId wychodzaca, Guid groupId)> ProposeAsync(
        CastellanDbContext db, AccountId z, AccountId na)
    {
        var groupId = Guid.NewGuid();
        var wychodzaca = Transaction.CreateManual(z, new Money(-Kwota), DateTimeOffset.Now, Category.UnsortedId);
        var przychodzaca = Transaction.CreateManual(na, new Money(Kwota), DateTimeOffset.Now, Category.UnsortedId);
        wychodzaca.ProposeTransfer(groupId);
        przychodzaca.ProposeTransfer(groupId);
        db.Transactions.AddRange(wychodzaca, przychodzaca);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (wychodzaca.Id, groupId);
    }

    private static DeleteTransactionUseCase Delete(CastellanDbContext db) =>
        new(new TransactionRepository(db), new UnitOfWork(db));

    private static Task<IReadOnlyList<AccountWithBalance>> Balances(CastellanDbContext db) =>
        new GetAccountsWithBalancesUseCase(
            new AccountRepository(db), new TransactionRepository(db)).ExecuteAsync();

    private static async Task<(CastellanDbContext db, AccountId z, AccountId na)> SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}").Options;

        var db = new CastellanDbContext(options);
        await db.Database.MigrateAsync();

        var kiedys = DateTimeOffset.UtcNow.AddYears(-1);
        var z = Account.Create("ING", AccountKind.Checking, new Money(Saldo), kiedys);
        var na = Account.Create("Revolut", AccountKind.Checking, new Money(Saldo), kiedys);
        db.Accounts.AddRange(z, na);
        db.Categories.Add(Category.Create(ConfirmTransferUseCase.ReserveCategoryName, CategoryKind.Expense));

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return (db, z.Id, na.Id);
    }

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"castellan_delprop_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
