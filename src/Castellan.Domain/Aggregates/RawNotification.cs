namespace Castellan.Domain.Aggregates;

public class RawNotification
{
    public RawNotificationId Id { get; private set; }
    public string PackageName { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Text { get; private set; } = "";
    public DateTimeOffset PostedAt { get; private set; }
    public ParseStatus ParseStatus { get; private set; }
    public TransactionId? TransactionId { get; private set; }

    private RawNotification() { }

    public static RawNotification CreateUnparsed(
        string packageName, string title, string text, DateTimeOffset postedAt) =>
        new() { Id = RawNotificationId.New(), PackageName = packageName, Title = title, Text = text, PostedAt = postedAt, ParseStatus = ParseStatus.Unparsed };

    public static RawNotification CreateIgnored(
        string packageName, string title, string text, DateTimeOffset postedAt) =>
        new() { Id = RawNotificationId.New(), PackageName = packageName, Title = title, Text = text, PostedAt = postedAt, ParseStatus = ParseStatus.Ignored };

    public void MarkParsed(TransactionId transactionId)
    {
        ParseStatus = ParseStatus.Parsed;
        TransactionId = transactionId;
    }
}
