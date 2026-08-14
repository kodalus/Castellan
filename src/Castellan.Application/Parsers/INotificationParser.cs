using Castellan.Domain.ValueObjects;

namespace Castellan.Application.Parsers;

// AccountHint: tekst wskazujący, którego konta dotyczy płatność — potrzebny dla
// powiadomień Portfela Google, które (w przeciwieństwie do apki banku) nie
// mówią same z siebie, z jakiego banku jest karta; tylko treść powiadomienia
// ("karta Revolut Wspólny") to zdradza.
public sealed record ParsedTransaction(Money Amount, string? Merchant, string? AccountHint = null);

public interface INotificationParser
{
    string PackageName { get; }
    ParsedTransaction? TryParse(string title, string text);
}
