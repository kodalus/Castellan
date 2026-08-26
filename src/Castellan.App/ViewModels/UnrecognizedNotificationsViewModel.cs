using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Castellan.Application.UseCases;
using Castellan.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public sealed class UnrecognizedRow
{
    public RawNotificationId Id { get; }
    public string Header { get; }
    public string Title { get; }
    public string Text { get; }
    public string AmountDisplay { get; }
    public ICommand AddCommand { get; }
    public ICommand IgnoreCommand { get; }

    public UnrecognizedRow(UnrecognizedNotification n, ICommand add, ICommand ignore)
    {
        Id = n.Id;
        Header = $"{n.SourceName} · {n.PostedAt.ToLocalTime():dd.MM HH:mm}";
        Title = n.Title;
        Text = n.Text;
        AmountDisplay = n.Amount.ToString();
        AddCommand = add;
        IgnoreCommand = ignore;
    }
}

public partial class UnrecognizedNotificationsViewModel : ObservableObject
{
    private readonly GetUnrecognizedNotificationsUseCase _get;
    private readonly IgnoreRawNotificationUseCase _ignore;

    public ObservableCollection<UnrecognizedRow> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEmpty))]
    private bool _isEmpty = true;

    public bool IsNotEmpty => !IsEmpty;

    public UnrecognizedNotificationsViewModel(
        GetUnrecognizedNotificationsUseCase get,
        IgnoreRawNotificationUseCase ignore)
    {
        _get = get;
        _ignore = ignore;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        Rows.Clear();
        foreach (var n in await _get.ExecuteAsync(ct))
        {
            var captured = n;
            Rows.Add(new UnrecognizedRow(
                n,
                new AsyncRelayCommand(() => AddManuallyAsync(captured)),
                new AsyncRelayCommand(() => IgnoreAsync(captured.Id))));
        }
        IsEmpty = Rows.Count == 0;
    }

    /// <summary>
    /// Kwota jedzie do formularza jako podpowiedź, bo to jedyna rzecz, którą udało się
    /// z treści odczytać na pewno. Kierunku nie znamy — nierozpoznana treść z definicji
    /// nie powiedziała, czy to wydatek, czy wpływ.
    /// </summary>
    private static async Task AddManuallyAsync(UnrecognizedNotification n)
    {
        var amount = (n.Amount.Grosze / 100m).ToString("F2", CultureInfo.InvariantCulture);
        await Shell.Current.GoToAsync($"addTransaction?amount={amount}");
    }

    private async Task IgnoreAsync(RawNotificationId id)
    {
        await _ignore.ExecuteAsync(id);
        var row = Rows.FirstOrDefault(r => r.Id == id);
        if (row is not null) Rows.Remove(row);
        IsEmpty = Rows.Count == 0;
    }
}
