using TradeFlow.Domain.Common;

namespace TradeFlow.Domain.Catalog.Events;

public sealed record ProductCreatedDomainEvent(
    ProductId ProductId,
    string Sku,
    string Name,
    decimal UnitPrice,
    string Currency,
    DateTimeOffset OccurredOn) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
}

public sealed record ProductPriceChangedDomainEvent(
    ProductId ProductId,
    string Sku,
    decimal PreviousPrice,
    decimal NewPrice,
    string Currency,
    DateTimeOffset OccurredOn) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
}
