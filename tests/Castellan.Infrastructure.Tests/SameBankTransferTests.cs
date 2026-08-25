using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Castellan.Infrastructure.Data;
using Castellan.Infrastructure.Parsers;
using Castellan.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Tests;

/// <summary>
/// Przelew między dwoma kontami w TYM SAMYM banku, gdy docierają WYŁĄCZNIE powiadomienia
/// o pojedynczych nogach („1 PLN mniej na Twoim koncie"). Taka treść nie mówi ani słowa
/// o tym, którego konta dotyczy, więc przy dwóch kontach w banku nie ma z czego wybrać
/// i obie nogi lądują na tym samym koncie. Propozycja przelewu wymaga dwóch RÓŻNYCH kont,
/// więc nie powstaje, a ścieżka „odkładasz czy przekładasz" jest nieosiągalna.
///
/// To ograniczenie samej treści, nie dopasowania. Gdy bank przyśle powiadomienie zbiorcze
/// („z konta X na konto Y"), para powstaje poprawnie — pilnuje tego IngOwnTransferTests.
/// </summary>
public class SameBankTransferTests
{
    private const string IngPackage = "pl.ing.mojeing";

    private static async Task<(CastellanDbContext db, IngestRawNotificationUseCase ingest, GetTransferProposalsUseCase proposals)>
        SetupAsync(string dbPath, string? bankKey)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        var now = DateTimeOffset.UtcNow.AddYears(-1);
        db.Accounts.Add(Account.Create("ING", AccountKind.Checking, new Money(100_000), now, bankKey));
        db.Accounts.Add(Account.Create("OKO ING", AccountKind.Savings, Money.Zero, now, bankKey));
        db.Categories.Add(Category.Create("Produkty do domu", CategoryKind.Expense));
        db.Categories.Add(Category.Create("Rezerwy", CategoryKind.Expense));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var accountRepo = new AccountRepository(db);
        return (db,
            new IngestRawNotificationUseCase(
                new RawNotificationRepository(db),
                accountRepo,
                new TransactionRepository(db),
                new CategoryRuleRepository(db),
                new UnitOfWork(db),
                [new IngNotificationParser(), new RevolutNotificationParser(), new GoogleWalletNotificationParser()]),
            new GetTransferProposalsUseCase(new TransactionRepository(db), accountRepo));
    }

    private static async Task IngestBothLegsAsync(IngestRawNotificationUseCase ingest)
    {
        var now = DateTimeOffset.UtcNow;
        await ingest.ExecuteAsync(new IngestRawNotificationUseCase.Input(
            IngPackage, "Twój Asystent", "1 PLN mniej na Twoim koncie - Przelew własny", now));
        await ingest.ExecuteAsync(new IngestRawNotificationUseCase.Input(
            IngPackage, "Twój Asystent", "1 PLN więcej na Twoim koncie - Przelew własny", now.AddSeconds(5)));
    }

    private static async Task<string[]> AccountNamesOfAllAsync(CastellanDbContext db)
    {
        var txs = await db.Transactions.ToListAsync();
        var accounts = await db.Accounts.ToListAsync();
        return [.. txs.Select(t => accounts.First(a => a.Id == t.AccountId).Name)];
    }

    [Fact]
    public async Task Both_legs_land_on_the_same_account_so_no_transfer_is_proposed()
    {
        // Powiadomienie ING nie mówi ANI SŁOWA o tym, którego konta dotyczy — treść to
        // sama kwota, kierunek i sprzedawca. Przy dwóch kontach w ING nie ma z czego
        // wybrać, więc obie nogi trafiają na to samo konto rozliczeniowe.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_same_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, proposals) = await SetupAsync(dbPath, bankKey: null);

            await IngestBothLegsAsync(ingest);
            db.ChangeTracker.Clear();

            (await db.Transactions.CountAsync()).Should().Be(2, "dwie transakcje, tak jak zgłoszono");
            (await AccountNamesOfAllAsync(db)).Should().AllBe("ING",
                "obie nogi na koncie rozliczeniowym — nogi na OKO ING nie ma");

            (await proposals.ExecuteAsync()).Should().BeEmpty(
                "propozycja przelewu wymaga dwóch różnych kont");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Setting_the_bank_does_not_help_when_both_accounts_share_it()
    {
        // Pole „bank" rozstrzyga między BANKAMI, nie między kontami w jednym banku.
        // Tu oba konta są w ING, więc zawężenie niczego nie zawęża.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_same_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, proposals) = await SetupAsync(dbPath, Banks.Ing);

            await IngestBothLegsAsync(ingest);
            db.ChangeTracker.Clear();

            (await AccountNamesOfAllAsync(db)).Should().AllBe("ING");
            (await proposals.ExecuteAsync()).Should().BeEmpty();
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task When_the_legs_do_land_apart_the_proposal_knows_the_target_is_savings()
    {
        // Kontrola, że sama ścieżka pytania działa: gdy nogi trafią na różne konta
        // (bo powiadomienie niesie nazwę konta albo wpis powstał ręcznie), propozycja
        // mówi wprost, że cel jest oszczędnościowy — i skrzynka ma o co zapytać.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_same_{Guid.NewGuid():N}.db");
        try
        {
            var (db, _, proposals) = await SetupAsync(dbPath, Banks.Ing);

            var checking = await db.Accounts.SingleAsync(a => a.Name == "ING");
            var savings = await db.Accounts.SingleAsync(a => a.Name == "OKO ING");

            var groupId = Guid.NewGuid();
            var outgoing = Transaction.CreateManual(checking.Id, new Money(-100), DateTimeOffset.Now, Category.UnsortedId);
            var incoming = Transaction.CreateManual(savings.Id, new Money(100), DateTimeOffset.Now, Category.UnsortedId);
            outgoing.ProposeTransfer(groupId);
            incoming.ProposeTransfer(groupId);
            db.Transactions.AddRange(outgoing, incoming);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var list = await proposals.ExecuteAsync();
            var proposal = list.Should().ContainSingle().Subject;
            proposal.ToAccountName.Should().Be("OKO ING");
            proposal.ToIsSavings.Should().BeTrue();
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Revolut_to_savings_proposes_a_transfer_but_onto_the_wrong_account()
    {
        // Druga połowa zgłoszenia: przelew Z REVOLUTA na OKO ING. Tu nogi trafiają na
        // różne konta, więc propozycja przelewu POWSTAJE — ale noga przychodząca ląduje
        // na „ING", nie na „OKO ING", bo powiadomienie ING nie mówi o które konto chodzi.
        // Cel wychodzi więc rozliczeniowy i pytanie o rezerwę się nie pojawia.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_same_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, proposals) = await SetupAsync(dbPath, Banks.Ing);

            db.Accounts.Add(Account.Create("Revolut", AccountKind.Checking, new Money(100_000),
                DateTimeOffset.UtcNow.AddYears(-1), Banks.Revolut));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var now = DateTimeOffset.UtcNow;
            await ingest.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                "com.revolut.revolut", "Revolut", "Wydano 1,00 zł.", now));
            await ingest.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                IngPackage, "Twój Asystent", "1 PLN więcej na Twoim koncie - Przelew własny", now.AddSeconds(5)));
            db.ChangeTracker.Clear();

            var proposal = (await proposals.ExecuteAsync()).Should().ContainSingle().Subject;
            proposal.FromAccountName.Should().Be("Revolut");
            proposal.ToAccountName.Should().Be("ING", "noga przychodząca nie trafia na OKO ING");
            proposal.ToIsSavings.Should().BeFalse("stąd brak pytania o rezerwę");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_manual_transfer_can_be_marked_as_setting_money_aside()
    {
        // Droga, która działa NIEZALEŻNIE od tego, czy powiadomienia rozpoznały konta:
        // ręczny przelew zna oba konta z wyboru użytkownika, więc pytanie o rezerwę
        // da się tam zadać zawsze.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_same_{Guid.NewGuid():N}.db");
        try
        {
            var (db, _, _) = await SetupAsync(dbPath, Banks.Ing);

            var checking = await db.Accounts.SingleAsync(a => a.Name == "ING");
            var savings = await db.Accounts.SingleAsync(a => a.Name == "OKO ING");
            var fund = Fund.Create("Wakacje", FundKind.Vacation, new Money(500_000),
                DateOnly.FromDateTime(DateTime.Today).AddMonths(8));
            db.Funds.Add(fund);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var create = new CreateTransferUseCase(
                new AccountRepository(db), new TransactionRepository(db),
                new CategoryRepository(db), new FundRepository(db), new UnitOfWork(db));

            await create.ExecuteAsync(new CreateTransferUseCase.Input(
                checking.Id, savings.Id, new Money(40_000), DateTimeOffset.Now,
                Note: null, IsReserve: true, ContributeTo: fund.Id));
            db.ChangeTracker.Clear();

            var reserve = await db.Categories.SingleAsync(c => c.Name == "Rezerwy");
            var outgoing = await db.Transactions.SingleAsync(t => t.AccountId == checking.Id);
            outgoing.CategoryId.Should().Be(reserve.Id);
            outgoing.IsExcludedFromCalculations.Should().BeFalse("koperta Rezerwy ma to zobaczyć");

            var incoming = await db.Transactions.SingleAsync(t => t.AccountId == savings.Id);
            incoming.IsExcludedFromCalculations.Should().BeTrue("inaczej wyszłoby jako przychód");

            (await db.Funds.SingleAsync()).Balance.Grosze.Should().Be(40_000);
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
