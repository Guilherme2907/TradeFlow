namespace TradeFlow.Infrastructure.Messaging;

/// <summary>
/// Contract for an asynchronous integration event consumer hosted by the push-based
/// <see cref="RabbitMq.RabbitMqConsumerBackgroundService"/> and protected by the PostgreSQL Inbox Pattern (ADR-0011).
/// </summary>
public interface IIntegrationEventConsumer
{
    /// <summary>
    /// Unique logical name of the consumer used as part of the composite key <c>(message_id, consumer_name)</c>
    /// in <c>orders.inbox_messages</c>.
    /// </summary>
    string ConsumerName { get; }

    /// <summary>
    /// Durable RabbitMQ queue name dedicated to this consumer (with automatic <c>.dlq</c> dead-lettering).
    /// </summary>
    string QueueName { get; }

    /// <summary>
    /// Topic exchange routing key pattern (e.g., <c>orders.OrderSubmittedEvent</c> or <c>orders.*</c>).
    /// </summary>
    string RoutingKeyPattern { get; }

    /// <summary>
    /// Executes the consumer's business side-effects inside the Inbox transaction boundary.
    /// </summary>
    Task HandleAsync(
        Guid messageId,
        string eventType,
        string payload,
        CancellationToken cancellationToken = default);
}
