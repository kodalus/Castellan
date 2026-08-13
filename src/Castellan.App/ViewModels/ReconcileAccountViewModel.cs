using System.Globalization;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public partial class ReconcileAccountViewModel : ObservableObject, IQueryAttributable
{
    private AccountId _accountId;
    private readonly ReconcileAccountUseCase _reconcile;

    [ObservableProperty] private string _accountName = "";
    [ObservableProperty] private string _actualBalanceText = "";

    public ReconcileAccountViewModel(ReconcileAccountUseCase reconcile) => _reconcile = reconcile;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("accountId", out var id) && Guid.TryParse(id?.ToString(), out var guid))
            _accountId = new AccountId(guid);
        if (query.TryGetValue("name", out var name))
            AccountName = name?.ToString() ?? "";
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (!decimal.TryParse(ActualBalanceText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
            return;

        var grosze = (long)Math.Round(dec * 100, MidpointRounding.AwayFromZero);

        try
        {
            var result = await _reconcile.ExecuteAsync(
                new ReconcileAccountUseCase.Input(_accountId, new Money(grosze), DateTimeOffset.Now), ct);

            if (result.RequiresDecision && Shell.Current?.CurrentPage is Page p)
            {
                await p.DisplayAlertAsync(
                    "Nadwyżka salda",
                    $"Rozbieżność: {result.Discrepancy}.\nMożliwy niezapisany przychód lub zduplikowany wydatek. Uzgodnienie zapisano — sprawdź transakcje okresu.",
                    "OK");
            }

            await Shell.Current!.GoToAsync("..");
        }
        catch (Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            for (var e = ex; e != null; e = e.InnerException)
                sb.AppendLine($"[{e.GetType().Name}] {e.Message}");
            System.Diagnostics.Debug.WriteLine("[Reconcile] " + sb);
            if (Shell.Current?.CurrentPage is Page page)
                await page.DisplayAlertAsync("Błąd uzgodnienia", sb.ToString(), "OK");
        }
    }

    [RelayCommand]
    private static async Task CancelAsync() => await Shell.Current.GoToAsync("..");
}
