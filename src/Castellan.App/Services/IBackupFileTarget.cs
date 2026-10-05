namespace Castellan.App.Services;

/// <summary>
/// Oddaje gotową kopię zapasową użytkownikowi. Dwie platformy robią to zupełnie inaczej
/// i nie da się tego udawać jednym kodem: na telefonie plik idzie do arkusza
/// udostępniania (na dysk, na maila, gdziekolwiek), a na pulpicie do okna zapisu pliku,
/// gdzie wybiera się katalog i nazwę.
///
/// Arkusz udostępniania na pulpicie jest ślepą uliczką — Windows pokazuje go jako
/// okienko „Udostępnij" z listą aplikacji, a kopia zapasowa ma trafić do katalogu,
/// który użytkownik zapamięta i do którego wróci.
/// </summary>
public interface IBackupFileTarget
{
    /// <summary>
    /// Zwraca opis miejsca, w którym plik wylądował — do pokazania na ekranie — albo
    /// null, gdy użytkownik zrezygnował. Rezygnacja nie jest błędem.
    /// </summary>
    Task<string?> SaveAsync(string suggestedName, string content, CancellationToken ct = default);
}
