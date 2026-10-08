namespace TradeFlow.Domain.Common;

/// <summary>
/// Base class for Aggregate Roots that govern an invariant consistency boundary
/// and emit domain events.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId> where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Concurrency token utilized for Optimistic Concurrency Control (OCC).
    /// Maps to PostgreSQL row version / xmin in EF Core.
    /// </summary>
    public uint Version { get; protected set; }

    /// <summary>
    /// Point in time (UTC) when the aggregate was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; protected set; }

    /// <summary>
    /// Point in time (UTC) when the aggregate was last mutated.
    /// Updated when aggregate state changes to drive OCC and temporal audits.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; protected set; }

    protected AggregateRoot() { }

    protected AggregateRoot(TId id, DateTimeOffset? createdAt = null) : base(id)
    {
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marks the aggregate as mutated, updating the UpdatedAt timestamp.
    /// </summary>
    protected void Touch(DateTimeOffset? timestamp = null)
    {
        UpdatedAt = timestamp ?? DateTimeOffset.UtcNow;
    }

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}
