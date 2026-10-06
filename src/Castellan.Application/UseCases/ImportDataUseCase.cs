using System.Text.Json;
using Castellan.Application.Dto;
using Castellan.Application.Services;

namespace Castellan.Application.UseCases;

public sealed class ImportDataUseCase(IBackupService backup)
{
    /// <summary>
    /// Co wczytano. Import ZASTĘPUJE wszystko, więc jest nieodwracalny — a plik wybrany
    /// w telefonie wygląda z nazwy tak samo jak każdy inny. Komunikat „zakończono"
    /// nie odróżniał kopii sprzed godziny od kopii sprzed pół roku; rozjazd wychodził
    /// dopiero przy zaglądaniu do transakcji, gdy nie było już czego cofać.
    /// </summary>
    public sealed record Summary(
        string ExportedAt, int Accounts, int Transactions, int Categories,
        int Funds, int Assets, int Debts, int MonthBudgets)
    {
        public string Describe() =>
            $"Kopia z {ExportedAt}\n\n"
            + $"Konta: {Accounts}\n"
            + $"Transakcje: {Transactions}\n"
            + $"Kategorie: {Categories}\n"
            + $"Plany miesięcy: {MonthBudgets}\n"
            + $"Fundusze: {Funds}\n"
            + $"Aktywa: {Assets}\n"
            + $"Zobowiązania: {Debts}";
    }

    public async Task<Summary> ExecuteAsync(string json, CancellationToken ct = default)
    {
        var data = JsonSerializer.Deserialize<CastellanExport>(json)
            ?? throw new InvalidOperationException("Nieprawidłowy format pliku kopii zapasowej.");

        if (data.Version != 1)
            throw new InvalidOperationException($"Nieobsługiwana wersja kopii zapasowej: {data.Version}.");

        await backup.ImportAsync(data, ct);

        return new Summary(
            data.ExportedAt,
            data.Accounts.Count,
            data.Transactions.Count,
            data.Categories.Count(c => !c.IsSystem),
            data.Funds.Count,
            data.Assets.Count,
            (data.Debts ?? []).Count,
            data.MonthBudgets.Count);
    }
}
