using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using TradeFlow.Domain.Orders.Events;

namespace TradeFlow.Infrastructure.Messaging.Kafka;

/// <summary>
/// Centralizes Apache Kafka topic names, partition counts, Outbox routing rules, and idempotent topic initialization (ADR-0012).
/// </summary>
public static class KafkaTopology
{
    public const string TradesTopic = "tradeflow.market.trades.v1";
    public const string TradesDeadLetterTopic = "tradeflow.market.trades.dlq";
    public const string DefaultConsumerGroup = "tradeflow-market-analytics-group";
    public const int DefaultPartitionCount = 3;

    /// <summary>
    /// Resolves the target Kafka topic and extracts the deterministic Partition Key directly from the Outbox JSON payload.
    /// Returns <c>false</c> if the event is not destined for Kafka (e.g., RabbitMQ-only workflow events).
    /// Adding future events (e.g., Inventory stock changes or User activity streams) only requires adding a single route case here.
    /// </summary>
    public static bool TryGetOutboxRoute(
        string eventType,
        string payload,
        out string topic,
        out string partitionKey)
    {
        var route = eventType switch
        {
            // Orders / Market Trades -> partitioned by ProductId to preserve strict price sequence per asset
            nameof(OrderLineAddedEvent) => (Topic: TradesTopic, KeyPath: "productId.value"),

            // Future modules (e.g., Inventory, Users) simply declare their Topic and Partition Key path here:
            // nameof(StockAdjustedEvent) => (Topic: "tradeflow.inventory.stock.v1", KeyPath: "productId.value"),
            // nameof(UserLoggedInEvent)  => (Topic: "tradeflow.users.activity.v1",  KeyPath: "userId.value"),
            _ => (Topic: string.Empty, KeyPath: string.Empty)
        };

        if (string.IsNullOrEmpty(route.Topic))
        {
            topic = string.Empty;
            partitionKey = string.Empty;
            return false;
        }

        topic = route.Topic;
        partitionKey = ExtractJsonPropertyByPath(payload, route.KeyPath);
        return true;
    }

    private static string ExtractJsonPropertyByPath(string payload, string dotSeparatedPath)
    {
        using var document = JsonDocument.Parse(payload);
        var current = document.RootElement;

        foreach (var segment in dotSeparatedPath.Split('.'))
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return string.Empty;
            }
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString() ?? string.Empty
            : current.GetRawText();
    }

    public static async Task EnsureTopicsCreatedAsync(
        string bootstrapServers,
        int numPartitions = DefaultPartitionCount,
        short replicationFactor = 1)
    {
        var adminConfig = new AdminClientConfig
        {
            BootstrapServers = bootstrapServers
        };

        using var adminClient = new AdminClientBuilder(adminConfig).Build();
        var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(10));
        var existingTopics = metadata.Topics.Select(t => t.Topic).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var specsToCreate = new List<TopicSpecification>();

        if (!existingTopics.Contains(TradesTopic))
        {
            specsToCreate.Add(new TopicSpecification
            {
                Name = TradesTopic,
                NumPartitions = numPartitions,
                ReplicationFactor = replicationFactor
            });
        }

        if (!existingTopics.Contains(TradesDeadLetterTopic))
        {
            specsToCreate.Add(new TopicSpecification
            {
                Name = TradesDeadLetterTopic,
                NumPartitions = 1,
                ReplicationFactor = replicationFactor
            });
        }

        if (specsToCreate.Count > 0)
        {
            try
            {
                await adminClient.CreateTopicsAsync(specsToCreate);
            }
            catch (CreateTopicsException ex)
                when (ex.Results.All(r => r.Error.Code is ErrorCode.TopicAlreadyExists or ErrorCode.NoError))
            {
                // Topic already created concurrently by another node/worker; safe to ignore.
            }
        }
    }
}
