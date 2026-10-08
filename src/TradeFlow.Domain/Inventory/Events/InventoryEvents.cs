using TradeFlow.Domain.Common;

namespace TradeFlow.Domain.Inventory.Events;

public sealed record StockAllocatedDomainEvent(
    InventoryItemId InventoryItemId,
    ProductId ProductId,
    string Sku,
    int Quantity,
    DateTimeOffset OccurredOn) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
}

public sealed record StockReleasedDomainEvent(
    InventoryItemId InventoryItemId,
    ProductId ProductId,
    string Sku,
    int Quantity,
    DateTimeOffset OccurredOn) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
}

public sealed record StockReplenishedDomainEvent(
    InventoryItemId InventoryItemId,
    ProductId ProductId,
    string Sku,
    int Quantity,
    DateTimeOffset OccurredOn) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
}
