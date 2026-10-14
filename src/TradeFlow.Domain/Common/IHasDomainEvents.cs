namespace TradeFlow.Domain.Common;

/// <summary>
/// Exposes uncommitted domain events raised by Aggregate Roots
/// for persistence and outbox dispatching (ADR-0011).
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
