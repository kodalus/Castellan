using System.Text.RegularExpressions;
using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;

namespace Castellan.Application.UseCases;

public sealed partial class IngestRawNotificationUseCase(
    IRawNotificationRepository rawNotifications,
    IUnitOfWork uow)
{
    // Only these packages are ever recorded — everything else is silently discarded.
    public static readonly IReadOnlySet<string> AllowedPackages = new HashSet<string>(StringComparer.Ordinal)
    {
        "pl.ing.mojeing",                        // ING
        "com.revolut.revolut",                   // Revolut
        "com.google.android.apps.walletnfcrel",  // Google Wallet — always Ignored
    };

    // Google Wallet duplicates bank notifications but carries no transaction data.
    private static readonly IReadOnlySet<string> IgnoredPackages = new HashSet<string>(StringComparer.Ordinal)
    {
        "com.google.android.apps.walletnfcrel",
    };

    public sealed record Input(string PackageName, string Title, string Text, DateTimeOffset PostedAt);

    public async Task ExecuteAsync(Input input, CancellationToken ct = default)
    {
        if (!AllowedPackages.Contains(input.PackageName)) return;

        var maskedTitle = MaskSensitiveData(input.Title);
        var maskedText  = MaskSensitiveData(input.Text);

        var notification = IgnoredPackages.Contains(input.PackageName)
            ? RawNotification.CreateIgnored(input.PackageName, maskedTitle, maskedText, input.PostedAt)
            : RawNotification.CreateUnparsed(input.PackageName, maskedTitle, maskedText, input.PostedAt);

        await rawNotifications.AddAsync(notification, ct);
        await uow.SaveChangesAsync(ct);
    }

    // Masks 4–8 digit sequences not adjacent to a decimal separator (3DS codes, BLIK, OTP).
    private static string MaskSensitiveData(string text) =>
        SensitivePattern().Replace(text, "****");

    [GeneratedRegex(@"(?<![,.\d])\b\d{4,8}\b(?![,.\d])")]
    private static partial Regex SensitivePattern();
}
