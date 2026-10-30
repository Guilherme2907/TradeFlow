using Microsoft.Extensions.Logging;

namespace TradeFlow.Infrastructure.Messaging.Kafka;

/// <summary>
/// Outbox publisher adapter that streams Kafka-routed domain events directly from PostgreSQL
/// <c>orders.outbox_messages</c> to their target Kafka topic using the Outbox JSON payload as-is (ADR-0012).
/// Completely decoupled from specific event schemas: routing and partition key extraction are governed
/// centrally by <see cref="KafkaTopology.TryGetOutboxRoute"/>.
/// </summary>
public sealed class KafkaOutboxMessagePublisher : IOutboxMessagePublisher
{
    private readonly IKafkaTradeStreamProducer _kafkaProducer;
    private readonly ILogger<KafkaOutboxMessagePublisher> _logger;

    public KafkaOutboxMessagePublisher(
        IKafkaTradeStreamProducer kafkaProducer,
        ILogger<KafkaOutboxMessagePublisher> logger)
    {
        _kafkaProducer = kafkaProducer;
        _logger = logger;
    }

    public async Task PublishAsync(
        Guid messageId,
        string eventType,
        string payload,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        if (!KafkaTopology.TryGetOutboxRoute(eventType, payload, out var topic, out var partitionKey))
        {
            return;
        }

        var headers = new Dictionary<string, string>
        {
            ["x-event-id"] = messageId.ToString(),
            ["x-event-type"] = eventType,
            ["x-occurred-at"] = occurredAt.ToString("O")
        };

        var delivery = await _kafkaProducer.PublishRawAsync(
            topic,
            partitionKey,
            payload,
            headers,
            cancellationToken);

        _logger.LogDebug(
            "Streamed outbox event {MessageId} ({EventType}) directly to Kafka topic '{Topic}' [Partition: {Partition}, Offset: {Offset}, Key: {Key}]",
            messageId,
            eventType,
            delivery.Topic,
            delivery.Partition,
            delivery.Offset,
            partitionKey);
    }
}
