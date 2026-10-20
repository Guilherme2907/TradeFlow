using RabbitMQ.Client;

namespace TradeFlow.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Centralizes RabbitMQ exchange, routing key, and Dead-Letter Queue (DLQ) topology definitions (ADR-0011).
/// </summary>
public static class RabbitMqTopology
{
    public const string EventsExchangeName = "tradeflow.events";
    public const string DeadLetterExchangeName = "tradeflow.events.dlx";

    public static string GetOrdersRoutingKey(string eventType) => $"orders.{eventType}";

    /// <summary>
    /// Idempotently declares the durable topic exchange for domain integration events
    /// and its companion dead-letter exchange (DLX).
    /// </summary>
    public static async Task EnsureEventsExchangeDeclaredAsync(
        IChannel channel,
        CancellationToken cancellationToken = default)
    {
        await channel.ExchangeDeclareAsync(
            exchange: EventsExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange: DeadLetterExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Idempotently declares a durable consumer queue wired with Dead-Letter Exchange (DLX) routing
    /// and binds it to the main topic exchange using the specified routing pattern (e.g., <c>orders.OrderSubmittedEvent</c> or <c>orders.*</c>).
    /// </summary>
    public static async Task DeclareQueueWithDeadLetteringAsync(
        IChannel channel,
        string queueName,
        string routingKeyPattern,
        CancellationToken cancellationToken = default)
    {
        await EnsureEventsExchangeDeclaredAsync(channel, cancellationToken);

        var deadLetterQueueName = $"{queueName}.dlq";

        // 1. Declare Dead-Letter Queue (DLQ) and bind it to the Dead-Letter Exchange (DLX)
        await channel.QueueDeclareAsync(
            queue: deadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: deadLetterQueueName,
            exchange: DeadLetterExchangeName,
            routingKey: queueName,
            cancellationToken: cancellationToken);

        // 2. Declare primary queue with x-dead-letter-exchange and x-dead-letter-routing-key
        var queueArgs = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = DeadLetterExchangeName,
            ["x-dead-letter-routing-key"] = queueName
        };

        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArgs,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: queueName,
            exchange: EventsExchangeName,
            routingKey: routingKeyPattern,
            cancellationToken: cancellationToken);
    }
}
