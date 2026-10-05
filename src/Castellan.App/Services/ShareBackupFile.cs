namespace Castellan.App.Services;

/// <summary>
/// Telefon: plik ląduje w katalogu podręcznym i idzie do arkusza udostępniania, skąd
/// użytkownik wybiera, gdzie go wysłać. Katalog podręczny wystarcza, bo plik ma tam
/// przeżyć tylko do momentu wybrania celu — system może go potem sprzątnąć.
/// </summary>
public sealed class ShareBackupFile : IBackupFileTarget
{
    public async Task<string?> SaveAsync(
        string suggestedName, string content, CancellationToken ct = default)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, suggestedName);
        await File.WriteAllTextAsync(path, content, ct);

        await Share.RequestAsync(new ShareFileRequest
        {
            Title = "Kopia zapasowa Castellan",
            File = new ShareFile(path, "application/json"),
        });

        // Arkusz udostępniania nie mówi, co użytkownik wybrał — ani nawet czy wybrał.
        // Nazwa pliku to wszystko, co da się tu uczciwie pokazać.
        return suggestedName;
    }
}
