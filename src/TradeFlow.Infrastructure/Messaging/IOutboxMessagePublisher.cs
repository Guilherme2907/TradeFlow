namespace TradeFlow.Infrastructure.Messaging;

/// <summary>
/// Abstraction for publishing serialized outbox messages to the message broker (e.g., RabbitMQ)
/// with broker acknowledgment guarantees (Publisher Confirms) per ADR-0011.
/// </summary>
public interface IOutboxMessagePublisher
{
    Task PublishAsync(
        Guid messageId,
        string eventType,
        string payload,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);
}
