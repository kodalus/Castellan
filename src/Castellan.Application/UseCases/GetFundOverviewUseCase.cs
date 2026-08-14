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
    DateOnly Deadline,
    double Progress);

public sealed class GetFundOverviewUseCase(IFundRepository funds)
{
    public async Task<IReadOnlyList<FundSummary>> ExecuteAsync(int paydateDay, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var all   = await funds.ListAsync(ct);

        return [.. all
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
            .ThenBy(s => s.Deadline)];
    }

    private static string KindDisplay(FundKind kind) => kind switch
    {
        FundKind.Insurance => "Ubezpieczenie",
        FundKind.Vacation  => "Urlop",
        FundKind.Tax       => "Podatki",
        _                  => "Inny",
    };
}
