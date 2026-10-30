namespace TradeFlow.Infrastructure.Messaging;

/// <summary>
/// Composite outbox dispatcher that routes domain events directly from PostgreSQL <c>orders.outbox_messages</c>
/// to all active broker publishers (e.g., RabbitMQ for transactional task queues and Apache Kafka for event streaming).
/// Eliminates consumer-to-broker coupling and ensures each downstream consumer maintains a single responsibility (ADR-0011, ADR-0012).
/// </summary>
public sealed class RoutedOutboxMessagePublisher : IOutboxMessagePublisher
{
    private readonly IReadOnlyList<IOutboxMessagePublisher> _publishers;

    public RoutedOutboxMessagePublisher(IEnumerable<IOutboxMessagePublisher> publishers)
    {
        _publishers = publishers.ToList();
    }

    public async Task PublishAsync(
        Guid messageId,
        string eventType,
        string payload,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        foreach (var publisher in _publishers)
        {
            await publisher.PublishAsync(messageId, eventType, payload, occurredAt, cancellationToken);
        }
    }
}
