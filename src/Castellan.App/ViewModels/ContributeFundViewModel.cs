using System.Globalization;
using Castellan.Application.Repositories;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

[QueryProperty(nameof(FundId), "fundId")]
public partial class ContributeFundViewModel : ObservableObject
{
    private readonly IFundRepository _funds;
    private readonly ContributeToFundUseCase _contribute;

    [ObservableProperty] private string _fundId = "";
    [ObservableProperty] private string _fundName = "—";
    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    private FundId? _id;

    public ContributeFundViewModel(IFundRepository funds, ContributeToFundUseCase contribute)
    {
        _funds = funds;
        _contribute = contribute;
    }

    partial void OnFundIdChanged(string value)
    {
        if (Guid.TryParse(value, out var guid))
        {
            _id = new FundId(guid);
            _ = LoadNameAsync();
        }
    }

    private async Task LoadNameAsync()
    {
        if (_id is not { } id) return;
        var fund = await _funds.GetAsync(id);
        if (fund is not null) FundName = fund.Name;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (_id is not { } id) return;
        ErrorMessage = "";

        var grosze = ParseGrosze(AmountText);
        if (grosze <= 0) { ErrorMessage = "Podaj poprawną kwotę."; return; }

        IsBusy = true;
        try
        {
            await _contribute.ExecuteAsync(id, new Money(grosze), ct);
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static long ParseGrosze(string text)
    {
        var normalized = text.Trim().Replace(',', '.').Replace(" ", "");
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d > 0)
            return (long)Math.Round(d * 100, MidpointRounding.AwayFromZero);
        return 0;
    }
}
