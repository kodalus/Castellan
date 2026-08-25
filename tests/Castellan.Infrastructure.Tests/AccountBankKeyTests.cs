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
/// Nazwę konta użytkownik nadaje dla siebie, nie dla parsera — nie ma obowiązku wpisywać
/// w nią nazwy banku i nie sposób z góry wiedzieć, jaką konwencję wybierze. Jawnie
/// wskazany bank zdejmuje zgadywanie: dopasowanie zawęża się do kont tego banku, cokolwiek
/// stoi w nazwie. Bez wskazania działa dawne zgadywanie po nazwie, więc pole może zostać
/// puste i nic się nie psuje.
/// </summary>
public class AccountBankKeyTests
{
    private const string RevolutPackage = "com.revolut.revolut";
    private const string IngPackage = "pl.ing.mojeing";

    private static async Task<(CastellanDbContext db, IngestRawNotificationUseCase ingest, UpdateAccountUseCase update)>
        SetupAsync(string dbPath)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new CastellanDbContext(options);
        db.Database.Migrate();

        var now = DateTimeOffset.UtcNow.AddYears(-1);
        db.Accounts.Add(Account.Create("ING", AccountKind.Checking, Money.Zero, now, Banks.Ing));
        db.Accounts.Add(Account.Create("wspólne", AccountKind.Checking, Money.Zero, now));
        db.Accounts.Add(Account.Create("osobiste", AccountKind.Checking, Money.Zero, now));
        db.Categories.Add(Category.Create("Produkty do domu", CategoryKind.Expense));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var accountRepo = new AccountRepository(db);
        var uow = new UnitOfWork(db);

        return (db,
            new IngestRawNotificationUseCase(
                new RawNotificationRepository(db),
                accountRepo,
                new TransactionRepository(db),
                new CategoryRuleRepository(db),
                uow,
                [new IngNotificationParser(), new RevolutNotificationParser(), new GoogleWalletNotificationParser()]),
            new UpdateAccountUseCase(accountRepo, uow));
    }

    private static async Task<string> AccountNameOfLastAsync(CastellanDbContext db)
    {
        // Sortowanie po stronie klienta: SQLite nie umie ORDER BY po DateTimeOffset.
        var txs = await db.Transactions.ToListAsync();
        var tx = txs.OrderByDescending(t => t.OccurredAt).First();
        return (await db.Accounts.SingleAsync(a => a.Id == tx.AccountId)).Name;
    }

    private static async Task<Account> ByNameAsync(CastellanDbContext db, string name) =>
        await db.Accounts.SingleAsync(a => a.Name == name);

    [Fact]
    public async Task Without_a_bank_the_name_is_all_there_is_and_it_can_miss()
    {
        // Stan wyjściowy, spisany świadomie: konta Revoluta nie mówią o banku, a treść
        // powiadomienia nie mówi o koncie — trafia na pierwsze konto rozliczeniowe, ING.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_bank_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, _) = await SetupAsync(dbPath);

            await ingest.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                RevolutPackage, "Revolut", "Wydano 44,00 zł.", DateTimeOffset.UtcNow));

            (await AccountNameOfLastAsync(db)).Should().Be("ING");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task With_a_bank_a_prefixless_notification_stays_inside_that_bank()
    {
        // Ten sam przypadek co wyżej, po wskazaniu banku przy obu kontach Revoluta.
        // Nazwy zostają bez zmian — cała różnica siedzi w polu „bank". Wybór między
        // dwoma kontami tego samego banku pozostaje zgadywaniem, ale zgaduje się już
        // we WŁAŚCIWYM banku, a nie na cudzym koncie.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_bank_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, update) = await SetupAsync(dbPath);

            foreach (var name in new[] { "wspólne", "osobiste" })
            {
                var a = await ByNameAsync(db, name);
                await update.ExecuteAsync(new UpdateAccountUseCase.Input(
                    a.Id, a.Name, a.Kind, Banks.Revolut));
            }
            db.ChangeTracker.Clear();

            await ingest.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                RevolutPackage, "Revolut", "Wydano 44,00 zł.", DateTimeOffset.UtcNow));

            (await AccountNameOfLastAsync(db)).Should().NotBe("ING");
            (await ByNameAsync(db, "wspólne")).Name.Should().Be("wspólne", "nazwa nietknięta");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task A_bank_with_one_account_removes_the_guessing_entirely()
    {
        // Gdy w banku jest tylko JEDNO konto, wskazanie banku zdejmuje zgadywanie do
        // zera: nieważne, jak konto się nazywa i czy powiadomienie mówi cokolwiek
        // o koncie — pole ma jednego kandydata.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_bank_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, update) = await SetupAsync(dbPath);

            var joint = await ByNameAsync(db, "wspólne");
            await update.ExecuteAsync(new UpdateAccountUseCase.Input(
                joint.Id, "Portfel na dwoje", joint.Kind, Banks.Revolut));
            db.ChangeTracker.Clear();

            await ingest.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                RevolutPackage, "Revolut", "Wydano 44,00 zł.", DateTimeOffset.UtcNow));

            (await AccountNameOfLastAsync(db)).Should().Be("Portfel na dwoje",
                "nazwa nie mówi nic ani o banku, ani o niczym z treści — decyduje samo pole");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task The_bank_beats_a_misleading_name()
    {
        // Konto nazwane „Revolut na czarną godzinę", ale należące do ING. Zgadywanie po
        // nazwie wciągnęłoby na nie powiadomienia Revoluta; jawny bank na to nie pozwala.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_bank_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, update) = await SetupAsync(dbPath);

            var mislabeled = await ByNameAsync(db, "osobiste");
            await update.ExecuteAsync(new UpdateAccountUseCase.Input(
                mislabeled.Id, "Revolut na czarną godzinę", AccountKind.Savings, Banks.Ing));

            var joint = await ByNameAsync(db, "wspólne");
            await update.ExecuteAsync(new UpdateAccountUseCase.Input(
                joint.Id, joint.Name, joint.Kind, Banks.Revolut));
            db.ChangeTracker.Clear();

            await ingest.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                RevolutPackage, "Revolut", "Wydano 44,00 zł.", DateTimeOffset.UtcNow));

            (await AccountNameOfLastAsync(db)).Should().Be("wspólne",
                "bank rozstrzyga, mimo że cudza nazwa krzyczy „Revolut”");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Ing_notifications_are_unaffected_by_revolut_accounts_getting_a_bank()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_bank_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, update) = await SetupAsync(dbPath);

            var joint = await ByNameAsync(db, "wspólne");
            await update.ExecuteAsync(new UpdateAccountUseCase.Input(
                joint.Id, joint.Name, joint.Kind, Banks.Revolut));
            db.ChangeTracker.Clear();

            await ingest.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                IngPackage, "Twój Asystent", "44,00 PLN mniej na Twoim koncie - Lidl", DateTimeOffset.UtcNow));

            (await AccountNameOfLastAsync(db)).Should().Be("ING");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Renaming_an_account_leaves_its_transactions_where_they_are()
    {
        // Powód, dla którego edycja nazwy jest bezpieczna: transakcje wiszą na
        // identyfikatorze konta, nie na jego nazwie.
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_bank_{Guid.NewGuid():N}.db");
        try
        {
            var (db, ingest, update) = await SetupAsync(dbPath);

            await ingest.ExecuteAsync(new IngestRawNotificationUseCase.Input(
                IngPackage, "Twój Asystent", "44,00 PLN mniej na Twoim koncie - Lidl", DateTimeOffset.UtcNow));
            db.ChangeTracker.Clear();

            var ing = await ByNameAsync(db, "ING");
            var before = await db.Transactions.CountAsync(t => t.AccountId == ing.Id);
            before.Should().Be(1);

            await update.ExecuteAsync(new UpdateAccountUseCase.Input(
                ing.Id, "ING Konto z Lwem", AccountKind.Savings, Banks.Ing));
            db.ChangeTracker.Clear();

            var renamed = await ByNameAsync(db, "ING Konto z Lwem");
            renamed.Id.Should().Be(ing.Id);
            renamed.Kind.Should().Be(AccountKind.Savings);
            (await db.Transactions.CountAsync(t => t.AccountId == ing.Id)).Should().Be(before);
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
