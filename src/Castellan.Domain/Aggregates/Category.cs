namespace Castellan.Domain.Aggregates;

public class Category
{
    // Well-known IDs for system categories — created by migration seed
    public static readonly CategoryId UnsortedId    = new(new Guid("00000000-0000-7000-8000-000000000001"));
    public static readonly CategoryId UnidentifiedId = new(new Guid("00000000-0000-7000-8000-000000000002"));
    public static readonly CategoryId TransferId     = new(new Guid("00000000-0000-7000-8000-000000000003"));

    public CategoryId Id { get; private set; }
    public string Name { get; private set; } = "";
    public CategoryKind Kind { get; private set; }
    public bool IsSystem { get; private set; }
    public bool IsArchived { get; private set; }

    private Category() { }

    public static Category Create(string name, CategoryKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Category
        {
            Id = CategoryId.New(),
            Name = name.Trim(),
            Kind = kind,
            IsSystem = false,
            IsArchived = false,
        };
    }

    internal static Category CreateSystem(CategoryId id, string name, CategoryKind kind) =>
        new()
        {
            Id = id,
            Name = name,
            Kind = kind,
            IsSystem = true,
            IsArchived = false,
        };

    public void Rename(string name)
    {
        if (IsSystem) throw new InvalidOperationException("System categories cannot be renamed.");
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void Archive()
    {
        if (IsSystem) throw new InvalidOperationException("System categories cannot be archived.");
        IsArchived = true;
    }
}
