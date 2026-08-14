using System.Globalization;
using Castellan.Application.UseCases;
using Castellan.Domain;
using Castellan.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public sealed record FundKindItem(FundKind Kind, string Display);

public partial class AddFundViewModel : ObservableObject
{
    private readonly CreateFundUseCase _create;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private FundKindItem? _selectedKind;
    [ObservableProperty] private string _targetAmountText = "";
    [ObservableProperty] private DateTime _deadline = new DateTime(DateTime.Today.Year + 1, 1, 1);
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public List<FundKindItem> Kinds { get; } =
    [
        new(FundKind.Tax,       "Podatki"),
        new(FundKind.Insurance, "Ubezpieczenie"),
        new(FundKind.Vacation,  "Urlop"),
        new(FundKind.Custom,    "Inny"),
    ];

    public AddFundViewModel(CreateFundUseCase create)
    {
        _create = create;
        SelectedKind = Kinds[0];
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct = default)
    {
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(Name)) { ErrorMessage = "Podaj nazwę funduszu."; return; }
        if (SelectedKind is null)            { ErrorMessage = "Wybierz rodzaj funduszu."; return; }

        var target = ParseGrosze(TargetAmountText);
        if (target <= 0) { ErrorMessage = "Podaj poprawną kwotę docelową."; return; }

        var deadlineDate = DateOnly.FromDateTime(Deadline);
        if (deadlineDate <= DateOnly.FromDateTime(DateTime.Today))
        {
            ErrorMessage = "Termin musi być w przyszłości.";
            return;
        }

        IsBusy = true;
        try
        {
            var cmd = new CreateFundCommand(
                Name.Trim(),
                SelectedKind.Kind,
                new Money(target),
                deadlineDate);

            await _create.ExecuteAsync(cmd, ct);
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
