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
/// Przelew własny w ING opisuje JEDNO powiadomienie, które nazywa obie strony:
/// „1,00 PLN z konta Direct Rika na konto Otwarte Konto Oszczędnościowe".
/// To jedyny format, w którym bank w ogóle mówi, o które konta chodzi — pozostałe
/// („1 PLN mniej na Twoim koncie") nie zdradzają tego wcale.
///
/// Nazwy bankowe nie mają ani jednego wspólnego słowa z nazwami w aplikacji
/// („ING", „OKO ING"), więc dopasowanie musi radzić sobie inaczej: skrótowcem
/// (OKO = Otwarte Konto Oszczędnościowe) i eliminacją (skoro nie to, to drugie).
/// </summary>
public class IngOwnTransferTests
{
    private const string IngPackage = "pl.ing.mojeing";
    private const string Title = "Moje ING. Twój Asystent";
    private const string OwnTransferText =
        "1,00 PLN z konta Direct Rika na konto Otwarte Konto Oszczędnościowe";

    private static async Task<(CastellanDbContext db, IngestRawNotificationUseCase ingest, GetTransferProposalsUseCase proposals)>
        SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        var now = DateTimeOffset.UtcNow.AddYears(-1);
        db.Accounts.Add(Account.Create("ING", AccountKind.Checking, new Money(100_000), now, Banks.Ing));
        db.Accounts.Add(Account.Create("OKO ING", AccountKind.Savings, Money.Zero, now, Banks.Ing));
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

    private static IngestRawNotificationUseCase.Input OwnTransfer(DateTimeOffset at) =>
        new(IngPackage, Title, OwnTransferText, at);

    private static IngestRawNotificationUseCase.Input Debit(DateTimeOffset at) =>
        new(IngPackage, Title, "1 PLN mniej na Twoim koncie - Przelew własny", at);

    private static IngestRawNotificationUseCase.Input Credit(DateTimeOffset at) =>
        new(IngPackage, Title, "1 PLN więcej na Twoim koncie - Przelew własny", at);

    private static async Task<Dictionary<string, long>> AmountsByAccountAsync(CastellanDbContext db)
    {
        var txs = await db.Transactions.ToListAsync();
        var accounts = await db.Accounts.ToListAsync();
        return txs.ToDictionary(
            t => accounts.First(a => a.Id == t.AccountId).Name,
            t => t.Amount.Grosze);
    }

    [Fact]
    public async Task The_summary_notification_alone_builds_the_whole_pair()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_own_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, proposals) = await SetupAsync(dbPath);

            await ingest.ExecuteAsync(OwnTransfer(DateTimeOffset.UtcNow));
            db.ChangeTracker.Clear();

            (await AmountsByAccountAsync(db)).Should().BeEquivalentTo(
                new Dictionary<string, long> { ["ING"] = -100, ["OKO ING"] = 100 });

            var proposal = (await proposals.ExecuteAsync()).Should().ContainSingle().Subject;
            proposal.FromAccountName.Should().Be("ING");
            proposal.ToAccountName.Should().Be("OKO ING", "skrótowiec OKO to inicjały nazwy bankowej");
            proposal.ToIsSavings.Should().BeTrue("dzięki temu skrzynka zapyta o odkładanie");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Single_leg_notifications_do_not_add_a_third_or_fourth_entry(bool summaryFirst)
    {
        // Bank potrafi wysłać zarówno powiadomienie zbiorcze, jak i osobne o obciążeniu
        // i uznaniu. Kolejność jest loterią, więc obie muszą dawać ten sam wynik: dwa
        // wpisy, każdy na właściwym koncie.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_own_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, _) = await SetupAsync(dbPath);
            var now = DateTimeOffset.UtcNow;

            if (summaryFirst)
            {
                await ingest.ExecuteAsync(OwnTransfer(now));
                await ingest.ExecuteAsync(Debit(now.AddSeconds(3)));
                await ingest.ExecuteAsync(Credit(now.AddSeconds(6)));
            }
            else
            {
                await ingest.ExecuteAsync(Debit(now));
                await ingest.ExecuteAsync(Credit(now.AddSeconds(3)));
                await ingest.ExecuteAsync(OwnTransfer(now.AddSeconds(6)));
            }
            db.ChangeTracker.Clear();

            (await db.Transactions.CountAsync()).Should().Be(2);
            (await AmountsByAccountAsync(db)).Should().BeEquivalentTo(
                new Dictionary<string, long> { ["ING"] = -100, ["OKO ING"] = 100 });
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_round_trip_of_the_same_amount_stays_two_separate_transfers()
    {
        // Realny przebieg testowania: 1 zł tam i 1 zł z powrotem, kilka minut różnicy.
        // Deduplikacja odsiewa powiadomienia o tej samej kwocie w oknie 15 minut, więc
        // to jest dokładnie ten układ, w którym drugi przelew mógłby zniknąć jako rzekomy
        // duplikat pierwszego.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_own_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, proposals) = await SetupAsync(dbPath);
            var now = DateTimeOffset.UtcNow;

            await ingest.ExecuteAsync(OwnTransfer(now));
            await ingest.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                IngPackage, Title,
                "1,00 PLN z konta Otwarte Konto Oszczędnościowe na konto Direct Rika",
                now.AddMinutes(3)));
            db.ChangeTracker.Clear();

            (await db.Transactions.CountAsync()).Should().Be(4, "dwa przelewy po dwie nogi");
            (await proposals.ExecuteAsync()).Should().HaveCount(2);

            var txs = await db.Transactions.ToListAsync();
            txs.Sum(t => t.Amount.Grosze).Should().Be(0, "tam i z powrotem wychodzi na zero");

            var accounts = await db.Accounts.ToListAsync();
            var oko = accounts.Single(a => a.Name == "OKO ING").Id;
            txs.Where(t => t.AccountId == oko).Sum(t => t.Amount.Grosze).Should().Be(0);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task An_ordinary_payment_is_not_mistaken_for_an_own_transfer()
    {
        // Kontrola: nowy wzorzec nie może łapać zwykłych zakupów, bo z każdego robiłby
        // parę wpisów na dwóch kontach.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_own_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, proposals) = await SetupAsync(dbPath);

            await ingest.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                IngPackage, Title, "69,57 PLN mniej na Twoim koncie - Direct Rika - płatność BLIK",
                DateTimeOffset.UtcNow));
            db.ChangeTracker.Clear();

            var tx = await db.Transactions.SingleAsync();
            tx.Amount.Grosze.Should().Be(-6_957);
            (await proposals.ExecuteAsync()).Should().BeEmpty();
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_transfer_out_of_the_savings_account_goes_the_other_way()
    {
        // Kierunek musi wynikać z treści, a nie z rodzaju konta: wypłata z oszczędności
        // z powrotem na bieżące to ten sam format, tylko konta zamienione miejscami.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_own_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, proposals) = await SetupAsync(dbPath);

            await ingest.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                IngPackage, Title,
                "250,00 PLN z konta Otwarte Konto Oszczędnościowe na konto Direct Rika",
                DateTimeOffset.UtcNow));
            db.ChangeTracker.Clear();

            (await AmountsByAccountAsync(db)).Should().BeEquivalentTo(
                new Dictionary<string, long> { ["OKO ING"] = -25_000, ["ING"] = 25_000 });

            var proposal = (await proposals.ExecuteAsync()).Should().ContainSingle().Subject;
            proposal.FromAccountName.Should().Be("OKO ING");
            proposal.ToIsSavings.Should().BeFalse("cel jest rozliczeniowy, więc o rezerwę się nie pyta");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_real_purchase_for_the_same_amount_is_not_swallowed_by_the_transfer()
    {
        // Kontrola do pochłaniania pojedynczych powiadomień: opiera się ono na samej
        // kwocie, więc prawdziwy zakup za dokładnie tę samą złotówkę musi przeżyć.
        //
        // Zakup jest 20 minut później, bo NIEZALEŻNIE od tej zmiany działa starsze,
        // zapasowe okno deduplikacji: ta sama kwota co do grosza na tym samym koncie
        // w ciągu 15 minut jest uznawana za duplikat. To osobne, wcześniejsze
        // zachowanie — tu chodzi o to, żeby nowe pochłanianie go nie rozszerzyło.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_own_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, _) = await SetupAsync(dbPath);
            var now = DateTimeOffset.UtcNow;

            await ingest.ExecuteAsync(OwnTransfer(now));
            await ingest.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                IngPackage, Title, "1 PLN mniej na Twoim koncie - Biedronka - płatność BLIK",
                now.AddMinutes(20)));
            db.ChangeTracker.Clear();

            (await db.Transactions.CountAsync()).Should().Be(3,
                "dwie nogi przelewu plus prawdziwy zakup");
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
