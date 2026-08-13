using Castellan.Domain.ValueObjects;

namespace Castellan.Application.Parsers;

public sealed record ParsedTransaction(Money Amount, string? Merchant);

public interface INotificationParser
{
    string PackageName { get; }
    ParsedTransaction? TryParse(string title, string text);
}
