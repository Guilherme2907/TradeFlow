namespace TradeFlow.Infrastructure.Modules.Orders.Persistence.Outbox;

/// <summary>
/// Represents an outbox record stored within the 'orders' PostgreSQL schema
/// in the same local transaction as aggregate state mutations (ADR-0011).
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; private set; }
    public string EventType { get; private set; } = null!;
    public string Payload { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? Error { get; private set; }
    public int RetryCount { get; private set; }

    private OutboxMessage() { } // EF Core

    public OutboxMessage(
        Guid id,
        string eventType,
        string payload,
        DateTimeOffset occurredAt)
    {
        Id = id;
        EventType = eventType;
        Payload = payload;
        OccurredAt = occurredAt;
    }

    public void MarkAsProcessed(DateTimeOffset processedAt)
    {
        ProcessedAt = processedAt;
        Error = null;
    }

    public void RecordFailure(string error)
    {
        RetryCount++;
        Error = error;
    }
}
