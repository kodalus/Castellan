using Castellan.Application.Dto;
using Castellan.Application.Services;
using Castellan.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Services;

internal sealed class BackupService(CastellanDbContext db) : IBackupService
{
    // EF Core boxes a plain `DBNull.Value` as typeof(DBNull) and can't find a
    // store type mapping for it. Wrapping in a SqliteParameter with an explicit
    // SqliteType sidesteps EF's CLR-type inference entirely.
    private static SqliteParameter Null(object? value, SqliteType type) =>
        new() { Value = value ?? (object)DBNull.Value, SqliteType = type };

    public async Task<CastellanExport> ExportAsync(CancellationToken ct = default)
    {
        var accounts     = await db.Accounts.AsNoTracking().ToListAsync(ct);
        var categories   = await db.Categories.AsNoTracking().ToListAsync(ct);
        var rules        = await db.CategoryRules.AsNoTracking().ToListAsync(ct);
        var transactions = await db.Transactions.AsNoTracking().ToListAsync(ct);
        var budgets      = await db.MonthBudgets
            .Include(b => b.Envelopes)
            .Include(b => b.IncomePlans)
            .AsNoTracking().ToListAsync(ct);
        var funds        = await db.Funds.AsNoTracking().ToListAsync(ct);
        var assets       = await db.Assets.AsNoTracking().ToListAsync(ct);
        var debts        = await db.Debts.AsNoTracking().ToListAsync(ct);

        return new CastellanExport
        {
            ExportedAt = DateTimeOffset.UtcNow.ToString("O"),
            Accounts = accounts.Select(a => new AccountDto(
                a.Id.Value, a.Name, (int)a.Kind, (int)a.LiquidityTier,
                a.BankKey, a.IsArchived, a.LastReconciledBalance.Grosze,
                a.LastReconciledAt.ToString("O"))).ToList(),
            Categories = categories.Select(c => new CategoryDto(
                c.Id.Value, c.Name, (int)c.Kind, c.IsSystem, c.IsArchived)).ToList(),
            CategoryRules = rules.Select(r => new CategoryRuleDto(
                r.Id.Value, r.Pattern, r.CategoryId.Value, r.Origin.ToString(),
                r.HitCount, r.LastUsedAt?.ToString("O"))).ToList(),
            Transactions = transactions.Select(t => new TransactionDto(
                t.Id.Value, t.AccountId.Value, t.Amount.Grosze, t.OccurredAt.ToString("O"),
                t.CategoryId.Value, t.RawMerchant, t.MerchantKey, t.Note,
                (int)t.Source, (int)t.Kind,
                t.TransferGroupId, t.ProposedTransferGroupId,
                t.SupersededById?.Value, t.RawNotificationId,
                t.PaidFromFundId?.Value)).ToList(),
            MonthBudgets = budgets.Select(b => new MonthBudgetDto(
                b.Id.Value, b.Month.ToString(), b.AvailableFunds.Grosze, b.PlannedAt.ToString("O"),
                b.Envelopes.Select(e => new EnvelopeDto(e.CategoryId.Value, e.PlannedAmount.Grosze)).ToList(),
                b.IncomePlans.Select(p => new IncomePlanDto(p.CategoryId.Value, p.PlannedAmount.Grosze)).ToList())).ToList(),
            Funds = funds.Select(f => new FundDto(
                f.Id.Value, f.Name, f.Kind.ToString(), f.TargetAmount.Grosze,
                f.StartMonth.ToString("yyyy-MM-dd"), f.Deadline.ToString("yyyy-MM-dd"), f.Balance.Grosze, f.IsArchived)).ToList(),
            Assets = assets.Select(a => new AssetDto(
                a.Id.Value, a.Name, a.Liquidity.ToString(), a.Value.Grosze,
                a.UpdatedOn.ToString("yyyy-MM-dd"), a.IsArchived)).ToList(),
            Debts = debts.Select(d => new DebtDto(
                d.Id.Value, d.Name, d.Kind.ToString(), d.InitialAmount.Grosze,
                d.Balance.Grosze, d.InstallmentAmount.Grosze, d.IsArchived)).ToList(),
        };
    }

    public async Task ImportAsync(CastellanExport data, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Clear in FK-safe order
            await db.Database.ExecuteSqlRawAsync("DELETE FROM IncomePlans", ct);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM Envelopes", ct);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM Transactions", ct);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM Reconciliations", ct);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM MonthBudgets", ct);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM Funds", ct);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM Assets", ct);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM Debts", ct);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM CategoryRules", ct);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM Categories WHERE IsSystem = 0", ct);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM Accounts", ct);

            foreach (var a in data.Accounts)
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Accounts (Id, Name, Kind, LiquidityTier, BankKey, IsArchived, LastReconciledBalance, LastReconciledAt) VALUES ({0},{1},{2},{3},{4},{5},{6},{7})",
                    a.Id, a.Name, a.Kind, a.LiquidityTier,
                    Null(a.BankKey, SqliteType.Text),
                    a.IsArchived ? 1 : 0, a.LastReconciledBalance, a.LastReconciledAt);

            foreach (var c in data.Categories.Where(c => !c.IsSystem))
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Categories (Id, Name, Kind, IsSystem, IsArchived) VALUES ({0},{1},{2},{3},{4})",
                    c.Id, c.Name, c.Kind, 0, c.IsArchived ? 1 : 0);

            foreach (var r in data.CategoryRules)
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO CategoryRules (Id, Pattern, CategoryId, Origin, HitCount, LastUsedAt) VALUES ({0},{1},{2},{3},{4},{5})",
                    r.Id, r.Pattern, r.CategoryId, r.Origin, r.HitCount,
                    Null(r.LastUsedAt, SqliteType.Text));

            foreach (var t in data.Transactions)
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Transactions (Id, AccountId, Amount, OccurredAt, CategoryId, RawMerchant, MerchantKey, Note, Source, Kind, TransferGroupId, ProposedTransferGroupId, SupersededById, RawNotificationId, PaidFromFundId) VALUES ({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14})",
                    t.Id, t.AccountId, t.Amount, t.OccurredAt, t.CategoryId,
                    Null(t.RawMerchant, SqliteType.Text),
                    Null(t.MerchantKey, SqliteType.Text),
                    Null(t.Note, SqliteType.Text),
                    t.Source, t.Kind,
                    Null(t.TransferGroupId, SqliteType.Text),
                    Null(t.ProposedTransferGroupId, SqliteType.Text),
                    Null(t.SupersededById, SqliteType.Text),
                    Null(t.RawNotificationId, SqliteType.Text),
                    Null(t.PaidFromFundId, SqliteType.Text));

            foreach (var b in data.MonthBudgets)
            {
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO MonthBudgets (Id, Month, AvailableFunds, PlannedAt) VALUES ({0},{1},{2},{3})",
                    b.Id, b.Month, b.AvailableFunds, b.PlannedAt);
                foreach (var e in b.Envelopes)
                    await db.Database.ExecuteSqlRawAsync(
                        "INSERT INTO Envelopes (Id, MonthBudgetId, CategoryId, PlannedAmount) VALUES ({0},{1},{2},{3})",
                        Guid.CreateVersion7(), b.Id, e.CategoryId, e.PlannedAmount);

                // Null-safe: kopie sprzed dodania planów przychodów nie mają tej sekcji.
                foreach (var p in b.IncomePlans ?? [])
                    await db.Database.ExecuteSqlRawAsync(
                        "INSERT INTO IncomePlans (Id, MonthBudgetId, CategoryId, PlannedAmount) VALUES ({0},{1},{2},{3})",
                        Guid.CreateVersion7(), b.Id, p.CategoryId, p.PlannedAmount);
            }

            foreach (var f in data.Funds)
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Funds (Id, Name, Kind, TargetAmount, StartMonth, Deadline, Balance, IsArchived) VALUES ({0},{1},{2},{3},{4},{5},{6},{7})",
                    f.Id, f.Name, f.Kind, f.TargetAmount, f.StartMonth, f.Deadline, f.Balance, f.IsArchived ? 1 : 0);

            foreach (var a in data.Assets)
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Assets (Id, Name, Liquidity, Value, UpdatedOn, IsArchived) VALUES ({0},{1},{2},{3},{4},{5})",
                    a.Id, a.Name, a.Liquidity, a.Value, a.UpdatedOn, a.IsArchived ? 1 : 0);

            // Null-safe: kopie sprzed dodania modułu długów nie mają tej sekcji.
            foreach (var d in data.Debts ?? [])
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Debts (Id, Name, Kind, InitialAmount, Balance, InstallmentAmount, IsArchived) VALUES ({0},{1},{2},{3},{4},{5},{6})",
                    d.Id, d.Name, d.Kind, d.InitialAmount, d.Balance, d.InstallmentAmount, d.IsArchived ? 1 : 0);

            await tx.CommitAsync(ct);

            // Powyższe INSERT/DELETE poszły surowym SQL i ominęły tracker EF — jeśli
            // ten sam (długo żyjący w MAUI) CastellanDbContext śledził jakiekolwiek
            // encje sprzed importu, teraz odnoszą się do wierszy, których import
            // już nie zna. Bez wyczyszczenia kolejny zapis na takiej encji kończy się
            // DbUpdateConcurrencyException, mimo że dane w bazie są poprawne.
            db.ChangeTracker.Clear();
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
