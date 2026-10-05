using Castellan.App.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Castellan.App.Platforms.Windows.Services;

/// <summary>
/// Pulpit: zwykłe okno zapisu pliku. Użytkownik wybiera katalog i nazwę, a potem widzi
/// pełną ścieżkę — bo kopia zapasowa jest wartościowa tylko wtedy, gdy wiadomo, gdzie
/// leży i da się do niej wrócić.
/// </summary>
public sealed class SaveDialogBackupFile : IBackupFileTarget
{
    public async Task<string?> SaveAsync(
        string suggestedName, string content, CancellationToken ct = default)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
        };
        picker.FileTypeChoices.Add("Kopia zapasowa Castellan", [".json"]);

        // Aplikacja jest niepakowana (bez MSIX), więc picker nie ma własnego okna
        // nadrzędnego i bez tego powiązania rzuca wyjątkiem zamiast się pokazać.
        if (ActiveWindowHandle() is { } handle && handle != IntPtr.Zero)
            InitializeWithWindow.Initialize(picker, handle);

        var file = await picker.PickSaveFileAsync();
        if (file is null) return null;

        await global::Windows.Storage.FileIO.WriteTextAsync(file, content);
        return file.Path;
    }

    private static IntPtr? ActiveWindowHandle()
    {
        // Pelna nazwa, bo samo „Application" celuje w namespace Castellan.Application.
        var platformWindow = Microsoft.Maui.Controls.Application.Current?.Windows
            .FirstOrDefault()?.Handler?.PlatformView;

        return platformWindow is Microsoft.UI.Xaml.Window native
            ? WindowNative.GetWindowHandle(native)
            : null;
    }
}
