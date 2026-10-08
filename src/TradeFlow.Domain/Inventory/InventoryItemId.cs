namespace TradeFlow.Domain.Inventory;

public readonly record struct InventoryItemId(Guid Value)
{
    public static InventoryItemId New() => new(Guid.NewGuid());
    public static InventoryItemId Empty => new(Guid.Empty);

    public override string ToString() => Value.ToString();
}
