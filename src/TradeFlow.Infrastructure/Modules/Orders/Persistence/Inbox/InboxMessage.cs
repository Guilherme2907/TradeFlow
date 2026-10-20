namespace TradeFlow.Infrastructure.Modules.Orders.Persistence.Inbox;

/// <summary>
/// Represents a deduplication record stored in <c>orders.inbox_messages</c> within the same local
/// database transaction as the consumer's business side-effects, guaranteeing idempotent processing
/// under At-Least-Once delivery (ADR-0011).
/// </summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; private set; }
    public string ConsumerName { get; private set; } = null!;
    public string EventType { get; private set; } = null!;
    public DateTimeOffset ProcessedAt { get; private set; }

    private InboxMessage() { } // EF Core

    public InboxMessage(
        Guid messageId,
        string consumerName,
        string eventType,
        DateTimeOffset processedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        MessageId = messageId;
        ConsumerName = consumerName;
        EventType = eventType;
        ProcessedAt = processedAt;
    }
}
