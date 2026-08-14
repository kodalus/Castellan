namespace Castellan.Domain.Aggregates;

public class CategoryRule
{
    public CategoryRuleId Id { get; private set; }
    public string Pattern { get; private set; } = "";
    public CategoryId CategoryId { get; private set; }
    public CategoryRuleOrigin Origin { get; private set; }
    public int HitCount { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }

    private CategoryRule() { }

    public static CategoryRule Create(
        string pattern,
        CategoryId categoryId,
        CategoryRuleOrigin origin = CategoryRuleOrigin.Manual) =>
        new()
        {
            Id = CategoryRuleId.New(),
            Pattern = pattern.Trim(),
            CategoryId = categoryId,
            Origin = origin,
            HitCount = 0,
        };

    public void RecordHit()
    {
        HitCount++;
        LastUsedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateCategory(CategoryId categoryId) => CategoryId = categoryId;

    public bool Matches(string? text) =>
        !string.IsNullOrEmpty(text) &&
        text.Contains(Pattern, StringComparison.OrdinalIgnoreCase);
}
