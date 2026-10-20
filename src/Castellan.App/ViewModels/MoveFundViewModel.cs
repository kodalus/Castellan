using System.Collections.ObjectModel;
using System.Globalization;
using Castellan.Application.Repositories;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

/// <summary>Fundusz do wyboru jako cel przeniesienia.</summary>
public sealed record FundChoice(FundId Id, string Name, string BalanceDisplay)
{
    public string Display => $"{Name} ({BalanceDisplay})";
}

[QueryProperty(nameof(FundId), "fundId")]
public partial class MoveFundViewModel : ObservableObject
{
    private readonly IFundRepository _funds;
    private readonly MoveBetweenFundsUseCase _move;

    [ObservableProperty] private string _fundId = "";
    [ObservableProperty] private string _fromName = "—";
    [ObservableProperty] private string _fromBalance = "";
    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<FundChoice> Targets { get; } = [];

    [ObservableProperty] private FundChoice? _target;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    private Castellan.Domain.FundId? _id;

    public MoveFundViewModel(IFundRepository funds, MoveBetweenFundsUseCase move)
    {
        _funds = funds;
        _move = move;
    }

    partial void OnFundIdChanged(string value)
    {
        if (Guid.TryParse(value, out var guid))
        {
            _id = new Castellan.Domain.FundId(guid);
            _ = LoadAsync();
        }
    }

    private async Task LoadAsync(CancellationToken ct = default)
    {
        if (_id is not { } id) return;

        var all = (await _funds.ListAsync(ct)).Where(f => !f.IsArchived).ToList();

        var source = all.FirstOrDefault(f => f.Id == id);
        if (source is null) return;

        FromName = source.Name;
        FromBalance = source.Balance.ToString();

        Targets.Clear();
        foreach (var f in all.Where(f => f.Id != id))
            Targets.Add(new FundChoice(f.Id, f.Name, f.Balance.ToString()));

        Target = Targets.FirstOrDefault();
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        if (_id is not { } id) return;
        ErrorMessage = "";

        if (Target is null) { ErrorMessage = "Nie ma drugiego funduszu, na który można przenieść."; return; }

        if (!decimal.TryParse(AmountText.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
        {
            ErrorMessage = "Podaj poprawną kwotę.";
            return;
        }

        var grosze = (long)Math.Round(Math.Abs(dec) * 100, MidpointRounding.AwayFromZero);

        IsBusy = true;
        try
        {
            await _move.ExecuteAsync(id, Target.Id, new Money(grosze), ct);
            if (Shell.Current is { } shell) await shell.GoToAsync("..");
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

    [RelayCommand]
    private static async Task CancelAsync() => await Shell.Current.GoToAsync("..");
}
