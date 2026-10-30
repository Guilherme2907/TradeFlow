using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace TradeFlow.Infrastructure.Messaging.Kafka;

/// <summary>
/// Thread-safe Singleton Kafka producer that streams <see cref="MarketTradeStreamEvent"/> records
/// to <see cref="KafkaTopology.TradesTopic"/> using <see cref="MarketTradeStreamEvent.ProductId"/>
/// as the Partition Key to guarantee strict chronological ordering per financial instrument (ADR-0012).
/// </summary>
public sealed class KafkaTradeStreamProducer : IKafkaTradeStreamProducer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaTradeStreamProducer> _logger;
    private bool _disposed;

    public KafkaTradeStreamProducer(
        string bootstrapServers,
        ILogger<KafkaTradeStreamProducer> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapServers);
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            // Wait for all in-sync replicas to acknowledge write
            Acks = Acks.All,
            // Guarantees exactly-once producer delivery per partition session (prevents duplicates on network retry)
            EnableIdempotence = true,
            MessageSendMaxRetries = 3,
            RetryBackoffMs = 100
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task<KafkaStreamDeliveryResult> PublishTradeAsync(
        MarketTradeStreamEvent tradeEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tradeEvent);

        // ADR-0012: Partition Key MUST be ProductId so all trades of the same asset land in the exact same partition
        var partitionKey = tradeEvent.ProductId.ToString();
        var payload = JsonSerializer.Serialize(tradeEvent, SerializerOptions);

        var headers = new Dictionary<string, string>
        {
            ["x-event-id"] = tradeEvent.EventId.ToString(),
            ["x-sku"] = tradeEvent.Sku
        };

        return await PublishRawAsync(
            KafkaTopology.TradesTopic,
            partitionKey,
            payload,
            headers,
            cancellationToken);
    }

    public async Task<KafkaStreamDeliveryResult> PublishRawAsync(
        string topic,
        string key,
        string rawPayload,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var kafkaHeaders = new Headers();
        if (headers is not null)
        {
            foreach (var kvp in headers)
            {
                kafkaHeaders.Add(kvp.Key, Encoding.UTF8.GetBytes(kvp.Value));
            }
        }

        var message = new Message<string, string>
        {
            Key = key,
            Value = rawPayload,
            Headers = kafkaHeaders
        };

        var deliveryResult = await _producer.ProduceAsync(topic, message, cancellationToken);

        _logger.LogDebug(
            "Published Kafka record to Topic '{Topic}' [Partition: {Partition}, Offset: {Offset}, Key: {Key}]",
            deliveryResult.Topic,
            deliveryResult.Partition.Value,
            deliveryResult.Offset.Value,
            key);

        return new KafkaStreamDeliveryResult(
            deliveryResult.Topic,
            deliveryResult.Partition.Value,
            deliveryResult.Offset.Value,
            key);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
