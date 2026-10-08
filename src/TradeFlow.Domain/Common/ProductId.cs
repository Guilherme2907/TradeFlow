namespace TradeFlow.Domain.Common;

/// <summary>
/// Cross-context identifier for a product in the catalog.
/// Referenced by Orders (snapshotting) and Inventory (stock tracking) without direct aggregate coupling.
/// </summary>
public readonly record struct ProductId(Guid Value)
{
    public static ProductId New() => new(Guid.NewGuid());
    public static ProductId Empty => new(Guid.Empty);

    public override string ToString() => Value.ToString();
}
