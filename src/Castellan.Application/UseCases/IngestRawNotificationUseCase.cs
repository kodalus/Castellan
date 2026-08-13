using System.Text.RegularExpressions;
using Castellan.Application.Parsers;
using Castellan.Application.Repositories;
using Castellan.Domain;
using Castellan.Domain.Aggregates;

namespace Castellan.Application.UseCases;

public sealed partial class IngestRawNotificationUseCase(
    IRawNotificationRepository rawNotifications,
    IAccountRepository accounts,
    ITransactionRepository transactions,
    ICategoryRuleRepository categoryRules,
    IUnitOfWork uow,
    IEnumerable<INotificationParser> parsers)
{
    public static readonly IReadOnlySet<string> AllowedPackages = new HashSet<string>(StringComparer.Ordinal)
    {
        "pl.ing.mojeing",
        "com.revolut.revolut",
        "com.google.android.apps.walletnfcrel",
    };

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

        if (notification.ParseStatus == ParseStatus.Unparsed)
        {
            var rules = await categoryRules.ListAsync(ct);
            await TryAutoParseAsync(notification, input.PackageName, input.PostedAt, rules, ct);
        }

        await uow.SaveChangesAsync(ct);
    }

    private async Task TryAutoParseAsync(
        RawNotification notification,
        string packageName,
        DateTimeOffset postedAt,
        IReadOnlyList<CategoryRule> rules,
        CancellationToken ct)
    {
        var parser = parsers.FirstOrDefault(p => p.PackageName == packageName);
        if (parser is null) return;

        var parsed = parser.TryParse(notification.Title, notification.Text);
        if (parsed is null) return;

        var account = await FindAccountAsync(packageName, ct);
        if (account is null) return;

        var tx = Transaction.CreateFromNotification(
            account.Id, parsed.Amount, postedAt, notification.Id, parsed.Merchant);

        var matchedRule = rules
            .OrderBy(r => r.Priority)
            .FirstOrDefault(r => r.Matches(parsed.Merchant));
        if (matchedRule is not null)
            tx.AssignCategory(matchedRule.CategoryId);

        await transactions.AddAsync(tx, ct);
        notification.MarkParsed(tx.Id);
    }

    private async Task<Account?> FindAccountAsync(string packageName, CancellationToken ct)
    {
        var all = await accounts.ListAsync(ct);
        var active = all.Where(a => !a.IsArchived).ToList();

        var keyword = packageName switch
        {
            "pl.ing.mojeing"      => "ING",
            "com.revolut.revolut" => "Revolut",
            _                     => null,
        };

        if (keyword is not null)
        {
            var byName = active.FirstOrDefault(a =>
                a.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            if (byName is not null) return byName;
        }

        return active.FirstOrDefault(a => a.Kind == AccountKind.Checking)
            ?? active.FirstOrDefault();
    }

    private static string MaskSensitiveData(string text) =>
        SensitivePattern().Replace(text, "****");

    [GeneratedRegex(@"(?<![,.\d])\b\d{4,8}\b(?![,.\d])")]
    private static partial Regex SensitivePattern();
}
