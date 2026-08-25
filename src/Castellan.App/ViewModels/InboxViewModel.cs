using System.Collections.ObjectModel;
using System.Windows.Input;
using Castellan.App.Services;
using Castellan.Application.Repositories;
using Castellan.Application.UseCases;
using Castellan.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public sealed record UnsortedTxRow(
    TransactionId Id,
    string MerchantDisplay,
    string AmountDisplay,
    string DateDisplay,
    System.Windows.Input.ICommand NavigateCommand);

public sealed record TransferProposalRow(
    Guid GroupId,
    string FromAccount,
    string ToAccount,
    string AmountDisplay,
    string DateDisplay,
    ICommand ConfirmCommand,
    ICommand RejectCommand);

public partial class InboxViewModel : ObservableObject
{
    private readonly ITransactionRepository _transactions;
    private readonly INotificationPermissionService _permission;
    private readonly GetTransferProposalsUseCase _getProposals;
    private readonly ConfirmTransferUseCase _confirmTransfer;
    private readonly RejectTransferUseCase _rejectTransfer;
    private readonly IFundRepository _funds;

    public ObservableCollection<UnsortedTxRow> Items { get; } = [];
    public ObservableCollection<TransferProposalRow> Proposals { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPermissionDenied))]
    private bool _isPermissionGranted = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEmpty))]
    private bool _isEmpty = true;

    [ObservableProperty] private bool _hasProposals;

    /// <summary>
    /// Tryb pracy. Bez tego wyboru osoba, która nie ma powiadomień bankowych i nie
    /// zamierza ich włączać, dostawała w kółko ostrzeżenie o braku uprawnienia —
    /// czyli nagabywanie o rzecz, której świadomie nie chce.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesManualEntry))]
    [NotifyPropertyChangedFor(nameof(IsPermissionDenied))]
    [NotifyPropertyChangedFor(nameof(EmptyStateText))]
    private bool _usesNotifications = Services.AppSettings.UsesNotifications;

    public bool UsesManualEntry
    {
        get => !UsesNotifications;
        set => UsesNotifications = !value;
    }

    partial void OnUsesNotificationsChanged(bool value)
    {
        Services.AppSettings.CaptureMode = value
            ? Services.CaptureMode.Notifications
            : Services.CaptureMode.Manual;
    }

    public bool IsNotEmpty => !IsEmpty;

    // W trybie ręcznym brak uprawnienia nie jest usterką, tylko stanem zamierzonym.
    public bool IsPermissionDenied => UsesNotifications && !IsPermissionGranted;

    public string EmptyStateText => UsesNotifications
        ? "Nic nie czeka na kategorię."
        : "W trybie ręcznym nic tu samo nie trafia. Wydatki dodajesz w zakładce Transakcje — „+” albo „⚡”.";

    public InboxViewModel(
        ITransactionRepository transactions,
        INotificationPermissionService permission,
        GetTransferProposalsUseCase getProposals,
        ConfirmTransferUseCase confirmTransfer,
        RejectTransferUseCase rejectTransfer,
        IFundRepository funds)
    {
        _transactions = transactions;
        _permission = permission;
        _getProposals = getProposals;
        _confirmTransfer = confirmTransfer;
        _rejectTransfer = rejectTransfer;
        _funds = funds;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsPermissionGranted = _permission.IsGranted();
        try
        {
            // Unsorted transactions
            Items.Clear();
            var list = await _transactions.ListUnsortedAsync(ct);
            foreach (var tx in list)
            {
                var txId = tx.Id;
                Items.Add(new UnsortedTxRow(
                    txId,
                    tx.RawMerchant ?? tx.MerchantKey ?? "—",
                    tx.Amount.ToString(),
                    tx.OccurredAt.ToLocalTime().ToString("dd.MM HH:mm"),
                    new AsyncRelayCommand(() =>
                        Shell.Current.GoToAsync($"assignCategory?txId={txId.Value}"))));
            }
            IsEmpty = Items.Count == 0;

            // Transfer proposals
            Proposals.Clear();
            var proposals = await _getProposals.ExecuteAsync(ct);
            foreach (var p in proposals)
            {
                var groupId = p.GroupId;
                var toIsSavings = p.ToIsSavings;
                Proposals.Add(new TransferProposalRow(
                    groupId,
                    p.FromAccountName,
                    p.ToAccountName,
                    p.Amount.ToString(),
                    p.OccurredAt.ToLocalTime().ToString("dd.MM HH:mm"),
                    new AsyncRelayCommand(() => ConfirmProposalAsync(groupId, toIsSavings)),
                    new AsyncRelayCommand(() => RejectProposalAsync(groupId))));
            }
            HasProposals = Proposals.Count > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[Inbox.Load] " + ex);
        }
    }

    /// <summary>
    /// Przelew na własne konto oszczędnościowe rozwidla się na dwa różne zdarzenia,
    /// nierozróżnialne dla aplikacji: przekładanie pieniędzy (nic się nie dzieje
    /// w budżecie) albo odkładanie na rezerwę (budżet miesiąca ubywa). Wie to tylko
    /// użytkownik, więc jest pytany — ale tylko wtedy, gdy cel naprawdę jest kontem
    /// oszczędnościowym.
    /// </summary>
    private async Task ConfirmProposalAsync(Guid groupId, bool toIsSavings)
    {
        if (toIsSavings && Shell.Current?.CurrentPage is Page page)
        {
            var isReserve = await page.DisplayAlertAsync(
                "Odkładasz na rezerwę?",
                "Przelew na konto oszczędnościowe może być odłożeniem pieniędzy "
                + "(obciąży kopertę „Rezerwy”) albo zwykłym przekładaniem między kontami "
                + "(zniknie z budżetu).",
                "Odkładam", "Przekładam");

            if (isReserve)
            {
                var fundId = await AskForFundAsync(page);
                await _confirmTransfer.ExecuteAsReserveAsync(groupId, fundId);
                RemoveProposal(groupId);
                return;
            }
        }

        await _confirmTransfer.ExecuteAsync(groupId);
        RemoveProposal(groupId);
    }

    /// <summary>
    /// Wskazanie funduszu jest dobrowolne: część rezerw nie ma jeszcze celu, a zmuszanie
    /// do wyboru zamieniłoby pytanie w przeszkodę. Pominięcie i tak obciąża kopertę.
    /// </summary>
    private async Task<FundId?> AskForFundAsync(Page page)
    {
        var active = (await _funds.ListAsync()).Where(f => !f.IsArchived).ToList();
        if (active.Count == 0) return null;

        var choice = await page.DisplayActionSheetAsync(
            "Na który fundusz?", "Bez funduszu", null, [.. active.Select(f => f.Name)]);
        if (string.IsNullOrEmpty(choice) || choice == "Bez funduszu") return null;

        return active.FirstOrDefault(f => f.Name == choice)?.Id;
    }

    private void RemoveProposal(Guid groupId)
    {
        var row = Proposals.FirstOrDefault(p => p.GroupId == groupId);
        if (row is not null) Proposals.Remove(row);
        HasProposals = Proposals.Count > 0;
    }

    private async Task RejectProposalAsync(Guid groupId)
    {
        await _rejectTransfer.ExecuteAsync(groupId);
        RemoveProposal(groupId);
    }

    [RelayCommand]
    private void OpenPermissionSettings() => _permission.OpenSettings();
}
