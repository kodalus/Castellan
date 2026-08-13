using System.Collections.ObjectModel;
using Castellan.App.Services;
using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public sealed record InboxRow(
    RawNotificationId Id,
    string PackageLabel,
    string Title,
    string Text,
    string DateDisplay);

public partial class InboxViewModel : ObservableObject
{
    private readonly IRawNotificationRepository _repo;
    private readonly INotificationPermissionService _permission;

    public ObservableCollection<InboxRow> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPermissionDenied))]
    private bool _isPermissionGranted = true;

    [ObservableProperty] private bool _isEmpty = true;

    public bool IsPermissionDenied => !IsPermissionGranted;

    public InboxViewModel(IRawNotificationRepository repo, INotificationPermissionService permission)
    {
        _repo = repo;
        _permission = permission;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsPermissionGranted = _permission.IsGranted();

        try
        {
            Items.Clear();
            var list = await _repo.ListUnparsedAsync(ct: ct);
            foreach (var n in list)
                Items.Add(new InboxRow(n.Id, PackageLabel(n.PackageName), n.Title, n.Text,
                    n.PostedAt.ToLocalTime().ToString("dd.MM HH:mm")));
            IsEmpty = Items.Count == 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[Inbox.Load] " + ex);
        }
    }

    [RelayCommand]
    private void OpenPermissionSettings() => _permission.OpenSettings();

    private static string PackageLabel(string packageName) => packageName switch
    {
        "pl.ing.mojeing"       => "ING",
        "com.revolut.revolut"  => "Revolut",
        _                      => packageName,
    };
}
