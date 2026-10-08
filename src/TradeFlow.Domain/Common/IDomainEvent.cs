namespace TradeFlow.Domain.Common;

/// <summary>
/// Marker interface for in-process domain events raised by Aggregate Roots.
/// Represents something meaningful that occurred within the domain boundary.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredOn { get; }
}
