using System.Collections.ObjectModel;
using System.Windows.Input;
using Castellan.Application.UseCases;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public sealed class DiscrepancyRow
{
    public string DateDisplay { get; }
    public string Merchant { get; }
    public string StoredDisplay { get; }
    public string ExpectedDisplay { get; }
    public string DifferenceDisplay { get; }
    public string CorrectLabel { get; }

    /// <summary>Poprawka jednym tapnięciem; niedostępna tam, gdzie kwota ma powiązania poza transakcją.</summary>
    public bool IsCorrectable { get; }
    public bool IsNotCorrectable => !IsCorrectable;
    public ICommand CorrectCommand { get; }

    /// <summary>Otwiera pełną edycję — gdy różnica jest zamierzona i trzeba zobaczyć kontekst.</summary>
    public ICommand OpenCommand { get; }

    public DiscrepancyRow(AmountDiscrepancy d, ICommand correct, ICommand open)
    {
        DateDisplay = d.OccurredAt.ToLocalTime().ToString("dd.MM.yyyy");
        Merchant = d.Merchant;
        StoredDisplay = $"jest {d.Stored}";
        ExpectedDisplay = $"w powiadomieniu {d.FromNotification}";
        DifferenceDisplay = d.Difference.Grosze > 0
            ? $"+{d.Difference}"
            : d.Difference.ToString();
        CorrectLabel = $"Popraw na {d.FromNotification}";
        IsCorrectable = d.IsCorrectable;
        CorrectCommand = correct;
        OpenCommand = open;
    }
}

public partial class NotificationAuditViewModel : ObservableObject
{
    private readonly AuditNotificationAmountsUseCase _audit;
    private readonly CorrectTransactionAmountUseCase _correct;

    public ObservableCollection<DiscrepancyRow> Items { get; } = [];

    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoResults))]
    private bool _hasRun;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoResults))]
    private bool _hasResults;

    [ObservableProperty] private string _summaryDisplay = "";
    [ObservableProperty] private string _unreadableDisplay = "";
    [ObservableProperty] private bool _hasUnreadable;

    public bool HasNoResults => HasRun && !HasResults;

    public NotificationAuditViewModel(
        AuditNotificationAmountsUseCase audit,
        CorrectTransactionAmountUseCase correct)
    {
        _audit = audit;
        _correct = correct;
    }

    /// <summary>
    /// Po poprawce przeliczamy raport od nowa: wiersz znika z listy, bo kwota zgadza się
    /// już z powiadomieniem. To jedyna informacja zwrotna, jakiej ten ekran potrzebuje —
    /// lista kurczy się do rzeczy, które faktycznie zostały do zrobienia.
    /// </summary>
    private async Task CorrectAsync(AmountDiscrepancy d)
    {
        try
        {
            await _correct.ExecuteAsync(d.TransactionId, d.FromNotification);
            await RunAsync();
        }
        catch (Exception ex)
        {
            if (Shell.Current?.CurrentPage is Page page)
                await page.DisplayAlertAsync("Nie udało się poprawić", ex.Message, "OK");
        }
    }

    [RelayCommand]
    private async Task RunAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            Items.Clear();
            var result = await _audit.ExecuteAsync(ct);

            foreach (var d in result.Discrepancies)
            {
                var captured = d;
                Items.Add(new DiscrepancyRow(
                    d,
                    new AsyncRelayCommand(() => CorrectAsync(captured)),
                    new AsyncRelayCommand(() =>
                        Shell.Current.GoToAsync($"editTransaction?txId={captured.TransactionId.Value}"))));
            }

            HasResults = Items.Count > 0;
            SummaryDisplay = Items.Count > 0
                ? $"Sprawdzono {result.CheckedCount} transakcji z powiadomień. Kwota nie zgadza się w {Items.Count}."
                : $"Sprawdzono {result.CheckedCount} transakcji z powiadomień. Wszystkie kwoty zgadzają się z treścią powiadomień.";

            HasUnreadable = result.UnreadableCount > 0;
            UnreadableDisplay = $"{result.UnreadableCount} powiadomień, których dzisiejszy wzorzec nie rozumie — tych nie dało się sprawdzić.";

            HasRun = true;
        }
        catch (Exception ex)
        {
            SummaryDisplay = "Nie udało się wykonać sprawdzenia: " + ex.Message;
            HasResults = false;
            HasRun = true;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
