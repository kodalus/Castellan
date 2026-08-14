using Castellan.Domain.ValueObjects;

namespace Castellan.Domain.Aggregates;

public class Asset
{
    public AssetId Id { get; private set; }
    public string Name { get; private set; } = "";
    public AssetLiquidity Liquidity { get; private set; }
    public Money Value { get; private set; }
    public DateOnly UpdatedOn { get; private set; }
    public bool IsArchived { get; private set; }

    private Asset() { }

    public static Asset Create(string name, AssetLiquidity liquidity, Money value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Asset
        {
            Id = AssetId.New(),
            Name = name.Trim(),
            Liquidity = liquidity,
            Value = value,
            UpdatedOn = DateOnly.FromDateTime(DateTime.Today),
            IsArchived = false,
        };
    }

    public void UpdateValue(Money newValue)
    {
        Value = newValue;
        UpdatedOn = DateOnly.FromDateTime(DateTime.Today);
    }

    public void Archive() => IsArchived = true;
}
