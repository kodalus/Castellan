namespace Castellan.Domain.Aggregates;

public class CategoryRule
{
    public CategoryRuleId Id { get; private set; }
    public string Pattern { get; private set; } = "";
    public CategoryId CategoryId { get; private set; }
    public int Priority { get; private set; }

    private CategoryRule() { }

    public static CategoryRule Create(string pattern, CategoryId categoryId, int priority = 100) =>
        new() { Id = CategoryRuleId.New(), Pattern = pattern.Trim(), CategoryId = categoryId, Priority = priority };

    public bool Matches(string? text) =>
        !string.IsNullOrEmpty(text) &&
        text.Contains(Pattern, StringComparison.OrdinalIgnoreCase);
}
