using Castellan.Domain.ValueObjects;

namespace Castellan.Application.Parsers;

// AccountHint: tekst wskazujący, którego konta dotyczy płatność — potrzebny dla
// powiadomień Portfela Google, które (w przeciwieństwie do apki banku) nie
// mówią same z siebie, z jakiego banku jest karta; tylko treść powiadomienia
// ("karta Revolut Wspólny") to zdradza.
public sealed record ParsedTransaction(Money Amount, string? Merchant, string? AccountHint = null);

/// <summary>
/// Przelew między własnymi kontami opisany JEDNYM powiadomieniem, które nazywa obie
/// strony („1,00 PLN z konta Direct Rika na konto Otwarte Konto Oszczędnościowe").
/// To jedyny znany format, w którym bank mówi wprost, o które konta chodzi — reszta
/// powiadomień ING nie zdradza tego wcale.
/// </summary>
public sealed record ParsedTransfer(Money Amount, string FromAccountHint, string ToAccountHint);

public interface INotificationParser
{
    string PackageName { get; }
    ParsedTransaction? TryParse(string title, string text);

    /// <summary>
    /// Zwraca parę kont, gdy powiadomienie opisuje przelew własny. Domyślnie brak —
    /// tylko ING ma taki format.
    /// </summary>
    ParsedTransfer? TryParseTransfer(string title, string text) => null;
}
