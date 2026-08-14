using Castellan.Application.UseCases;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public partial class BackupViewModel(ExportDataUseCase export, ImportDataUseCase import) : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _hasStatus;

    [RelayCommand]
    private async Task ExportAsync(CancellationToken ct = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        HasStatus = false;

        try
        {
            var json = await export.ExecuteAsync(ct);
            var fileName = $"castellan_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var filePath = Path.Combine(FileSystem.CacheDirectory, fileName);
            await File.WriteAllTextAsync(filePath, json, ct);

            await Share.RequestAsync(new ShareFileRequest
            {
                Title = "Kopia zapasowa Castellan",
                File = new ShareFile(filePath, "application/json"),
            });

            StatusMessage = $"Eksport gotowy: {fileName}";
            HasStatus = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Błąd eksportu: {ex.Message}";
            HasStatus = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportAsync(CancellationToken ct = default)
    {
        if (IsBusy) return;

        var confirmed = await Shell.Current.DisplayAlertAsync(
            "Importuj dane",
            "Import ZASTĄPI wszystkie obecne dane. Tej operacji nie można cofnąć. Kontynuować?",
            "Importuj", "Anuluj");

        if (!confirmed) return;

        IsBusy = true;
        HasStatus = false;

        try
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Wybierz plik kopii zapasowej",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, ["application/json"] },
                    { DevicePlatform.WinUI,   [".json"] },
                }),
            });

            if (result is null) return;

            string json;
            await using (var stream = await result.OpenReadAsync())
            using (var reader = new StreamReader(stream))
                json = await reader.ReadToEndAsync(ct);

            await import.ExecuteAsync(json, ct);

            StatusMessage = "Import zakończony. Uruchom aplikację ponownie lub przejdź do innej zakładki, aby odświeżyć dane.";
            HasStatus = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Błąd importu: {ex.Message}";
            HasStatus = true;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
