using Castellan.Application;
using Castellan.Application.Parsers;
using Castellan.Application.Repositories;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;
using Castellan.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Castellan.Infrastructure.Tests;

/// <summary>
/// Jedno powiadomienie doręczone dwa razy nie może założyć dwóch transakcji.
///
/// Android potrafi doręczyć to samo powiadomienie więcej niż raz — gdy apka banku
/// odświeża jego treść albo gdy po aktualizacji aplikacji nasłuch zostaje podpięty
/// dwa razy. Nasłuch nie czeka na wynik przetwarzania, więc dwa doręczenia oddalone
/// o milisekundy wykonują się RÓWNOLEGLE, każde z własnym DbContextem.
/// </summary>
public class DoubleDeliveryTests
{
    private static readonly IngestRawNotificationUseCase.Input BiedronkaPurchase = new(
        "pl.ing.mojeing",
        "Twój Asystent",
        "69,57 PLN mniej na Twoim koncie - Biedronka - płatność kartą",
        new DateTimeOffset(2026, 9, 3, 17, 42, 11, TimeSpan.FromHours(2)));

    [Fact]
    public async Task The_same_notification_delivered_twice_is_stored_once()
    {
        var dbPath = NewDbPath();
        try
        {
            var provider = await SetupAsync(dbPath);

            await DeliverAsync(provider, BiedronkaPurchase, rendezvous: null);
            await DeliverAsync(provider, BiedronkaPurchase, rendezvous: null);

            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CastellanDbContext>();

            // Deduplikacja transakcji sama by tu wystarczyła, ale dopiero PO założeniu
            // drugiego wpisu w Skrzynce — i po oznaczeniu go jako sparsowany wskazaniem
            // na transakcję, której nigdy nie dodano. Powtórka ma odpaść wcześniej.
            db.RawNotifications.Should().ContainSingle();
            db.Transactions.Should().ContainSingle();
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Two_simultaneous_deliveries_of_one_notification_create_one_transaction()
    {
        var dbPath = NewDbPath();
        try
        {
            var provider = await SetupAsync(dbPath);

            // Spotkanie ustawione w miejscu, w którym oba doręczenia skończyły już czytać
            // bazę, a żadne jeszcze nie zapisało — czyli dokładnie tam, gdzie na telefonie
            // powstawał duplikat. Gdy przetwarzanie idzie po kolei, drugie nigdy tu nie
            // dojdzie na czas i pierwsze rusza dalej po upływie limitu.
            var meeting = new Rendezvous(participants: 2, timeout: TimeSpan.FromSeconds(1));

            await Task.WhenAll(
                Task.Run(() => DeliverAsync(provider, BiedronkaPurchase, meeting)),
                Task.Run(() => DeliverAsync(provider, BiedronkaPurchase, meeting)));

            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CastellanDbContext>();

            db.Transactions.Should().ContainSingle();
            db.RawNotifications.Should().ContainSingle();
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static async Task DeliverAsync(
        IServiceProvider provider, IngestRawNotificationUseCase.Input input, Rendezvous? rendezvous)
    {
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        IRawNotificationRepository raws = sp.GetRequiredService<IRawNotificationRepository>();
        if (rendezvous is not null) raws = new MeetingRawNotifications(raws, rendezvous);

        var useCase = new IngestRawNotificationUseCase(
            raws,
            sp.GetRequiredService<IAccountRepository>(),
            sp.GetRequiredService<ITransactionRepository>(),
            sp.GetRequiredService<ICategoryRuleRepository>(),
            sp.GetRequiredService<IUnitOfWork>(),
            sp.GetServices<INotificationParser>());

        await useCase.ExecuteAsync(input);
    }

    private static async Task<ServiceProvider> SetupAsync(string dbPath)
    {
        var provider = new ServiceCollection().AddInfrastructure(dbPath).BuildServiceProvider();
        provider.ApplyMigrations();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CastellanDbContext>();
        db.Accounts.Add(Account.Create("ING", AccountKind.Checking, Money.Zero, DateTimeOffset.UtcNow.AddDays(-1)));
        await db.SaveChangesAsync();

        return provider;
    }

    /// <summary>
    /// Wpuszcza dalej dopiero, gdy zjawia się komplet uczestników — albo gdy upłynie
    /// limit czasu. Limit jest tym, co pozwala temu samemu testowi opisać oba światy:
    /// równoległy (komplet się zbiera) i szeregowy (nie zbiera się nigdy).
    /// </summary>
    private sealed class Rendezvous(int participants, TimeSpan timeout)
    {
        private readonly ManualResetEventSlim _gate = new(false);
        private int _arrived;

        public void Arrive()
        {
            if (Interlocked.Increment(ref _arrived) >= participants) _gate.Set();
            _gate.Wait(timeout);
        }
    }

    private sealed class MeetingRawNotifications(IRawNotificationRepository inner, Rendezvous meeting)
        : IRawNotificationRepository
    {
        public Task AddAsync(RawNotification notification, CancellationToken ct = default) =>
            inner.AddAsync(notification, ct);

        public Task<bool> ExistsAsync(
            string packageName, string title, string text, DateTimeOffset postedAt, CancellationToken ct = default) =>
            inner.ExistsAsync(packageName, title, text, postedAt, ct);

        public Task<RawNotification?> GetAsync(RawNotificationId id, CancellationToken ct = default) =>
            inner.GetAsync(id, ct);

        public Task<IReadOnlyList<RawNotification>> ListUnparsedAsync(int limit = 200, CancellationToken ct = default) =>
            inner.ListUnparsedAsync(limit, ct);

        public Task<int> CountByStatusAsync(ParseStatus status, CancellationToken ct = default) =>
            inner.CountByStatusAsync(status, ct);

        public Task<IReadOnlyList<RawNotification>> ListParsedAsync(CancellationToken ct = default) =>
            inner.ListParsedAsync(ct);

        /// <summary>Ostatni odczyt deduplikacji — tuż za nim zapada decyzja i idzie zapis.</summary>
        public async Task<IReadOnlyList<RawNotification>> ListParsedSinceAsync(
            DateTimeOffset since, CancellationToken ct = default)
        {
            var result = await inner.ListParsedSinceAsync(since, ct);
            meeting.Arrive();
            return result;
        }
    }

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"castellan_double_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { File.Delete(p); } catch (IOException) { }
    }
}
