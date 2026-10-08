using TradeFlow.Domain.Common;

namespace TradeFlow.Domain.Orders.Events;

public sealed record OrderCreatedEvent(
    OrderId OrderId,
    CustomerId CustomerId,
    string Currency,
    DateTimeOffset OccurredOn) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
}

public sealed record OrderLineAddedEvent(
    OrderId OrderId,
    OrderLineId LineId,
    ProductId ProductId,
    string Sku,
    Money UnitPrice,
    int Quantity,
    DateTimeOffset OccurredOn) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
}

public sealed record OrderSubmittedEvent(
    OrderId OrderId,
    CustomerId CustomerId,
    Money TotalAmount,
    DateTimeOffset OccurredOn) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
}

public sealed record OrderApprovedEvent(
    OrderId OrderId,
    DateTimeOffset OccurredOn) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
}

public sealed record OrderCancelledEvent(
    OrderId OrderId,
    string Reason,
    DateTimeOffset OccurredOn) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
}
