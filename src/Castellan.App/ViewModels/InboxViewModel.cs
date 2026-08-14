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

    public ObservableCollection<UnsortedTxRow> Items { get; } = [];
    public ObservableCollection<TransferProposalRow> Proposals { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPermissionDenied))]
    private bool _isPermissionGranted = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEmpty))]
    private bool _isEmpty = true;

    [ObservableProperty] private bool _hasProposals;

    public bool IsNotEmpty => !IsEmpty;
    public bool IsPermissionDenied => !IsPermissionGranted;

    public InboxViewModel(
        ITransactionRepository transactions,
        INotificationPermissionService permission,
        GetTransferProposalsUseCase getProposals,
        ConfirmTransferUseCase confirmTransfer,
        RejectTransferUseCase rejectTransfer)
    {
        _transactions = transactions;
        _permission = permission;
        _getProposals = getProposals;
        _confirmTransfer = confirmTransfer;
        _rejectTransfer = rejectTransfer;
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
                Proposals.Add(new TransferProposalRow(
                    groupId,
                    p.FromAccountName,
                    p.ToAccountName,
                    p.Amount.ToString(),
                    p.OccurredAt.ToLocalTime().ToString("dd.MM HH:mm"),
                    new AsyncRelayCommand(() => ConfirmProposalAsync(groupId)),
                    new AsyncRelayCommand(() => RejectProposalAsync(groupId))));
            }
            HasProposals = Proposals.Count > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[Inbox.Load] " + ex);
        }
    }

    private async Task ConfirmProposalAsync(Guid groupId)
    {
        await _confirmTransfer.ExecuteAsync(groupId);
        var row = Proposals.FirstOrDefault(p => p.GroupId == groupId);
        if (row is not null) Proposals.Remove(row);
        HasProposals = Proposals.Count > 0;
    }

    private async Task RejectProposalAsync(Guid groupId)
    {
        await _rejectTransfer.ExecuteAsync(groupId);
        var row = Proposals.FirstOrDefault(p => p.GroupId == groupId);
        if (row is not null) Proposals.Remove(row);
        HasProposals = Proposals.Count > 0;
    }

    [RelayCommand]
    private void OpenPermissionSettings() => _permission.OpenSettings();
}
