using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed record FundSummary(
    FundId Id,
    string Name,
    FundKind Kind,
    string KindDisplay,
    Money Balance,
    Money TargetAmount,
    Money Deficit,
    bool IsDelayed,
    Money SuggestedMonthly,
    int PeriodsRemaining,
    DateOnly? Deadline,
    double Progress)
{
    /// <summary>Fundusz otwarty: cel bez daty, zbierany aż uzbiera.</summary>
    public bool IsOpenEnded => Deadline is null;
}

/// <summary>Zakończony fundusz — tyle, ile trzeba, żeby go pokazać na liście i przywrócić.</summary>
public sealed record ClosedFundSummary(FundId Id, string Name, string KindDisplay, Money Balance);

public sealed record FundOverview(
    IReadOnlyList<FundSummary> Items,
    Money TotalBalance,
    Money TotalSuggestedMonthly,
    IReadOnlyList<ClosedFundSummary> Closed);

public sealed class GetFundOverviewUseCase(IFundRepository funds)
{
    public async Task<FundOverview> ExecuteAsync(int paydateDay, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var all   = await funds.ListAsync(ct);

        var items = all
            .Where(f => !f.IsArchived)
            .Select(f => new FundSummary(
                f.Id,
                f.Name,
                f.Kind,
                KindDisplay(f.Kind),
                f.Balance,
                f.TargetAmount,
                f.Deficit(today, paydateDay),
                f.IsDelayed(today, paydateDay),
                f.SuggestedMonthly(today, paydateDay),
                f.PeriodsRemaining(today, paydateDay),
                f.Deadline,
                f.Progress))
            .OrderBy(s => s.IsDelayed ? 0 : 1)
            .ThenBy(s => s.Deadline)
            .ToList();

        // Zakończone fundusze zostają widoczne osobno: inaczej „zakończ” byłoby nie do
        // odróżnienia od „usuń” — koperta znika z ekranu i nie ma jak sprawdzić, czy
        // poszła tam, gdzie miała, ani jak cofnąć pomyłki.
        var closed = all
            .Where(f => f.IsArchived)
            .OrderBy(f => f.Name)
            .Select(f => new ClosedFundSummary(f.Id, f.Name, KindDisplay(f.Kind), f.Balance))
            .ToList();

        return new FundOverview(
            items,
            new Money(items.Sum(s => s.Balance.Grosze)),
            // Suma odpisów = podpowiedź kwoty koperty "Rezerwy" na najbliższy miesiąc.
            new Money(items.Sum(s => s.SuggestedMonthly.Grosze)),
            closed);
    }

    private static string KindDisplay(FundKind kind) => kind switch
    {
        FundKind.Insurance => "Ubezpieczenie",
        FundKind.Vacation  => "Urlop",
        FundKind.Tax       => "Podatki",
        FundKind.Emergency => "Poduszka bezpieczeństwa",
        _                  => "Inny",
    };
}
