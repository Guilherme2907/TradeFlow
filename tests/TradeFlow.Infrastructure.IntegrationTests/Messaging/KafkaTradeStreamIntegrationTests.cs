using System.Text;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.Kafka;
using TradeFlow.Infrastructure.Messaging.Kafka;
using Xunit;

namespace TradeFlow.Infrastructure.IntegrationTests.Messaging;

/// <summary>
/// ADR-0012: End-to-end integration tests against a real containerized Apache Kafka broker verifying:
/// 1. Deterministic Partition Key routing (ProductId -> identical partition &amp; sequential offsets).
/// 2. Strict chronological consumption order per financial instrument and manual offset commits.
/// 3. Non-blocking Poison Pill isolation into Dead-Letter Topic (tradeflow.market.trades.dlq) preventing Head-of-Line Blocking.
/// </summary>
[Trait("Category", "RequiresDocker")]
public class KafkaTradeStreamIntegrationTests : IAsyncLifetime
{
    private readonly KafkaContainer _kafkaContainer = new KafkaBuilder("confluentinc/cp-kafka:7.6.1")
        .Build();

    private string _bootstrapServers = string.Empty;

    public async Task InitializeAsync()
    {
        await _kafkaContainer.StartAsync();
        _bootstrapServers = _kafkaContainer.GetBootstrapAddress();
        await KafkaTopology.EnsureTopicsCreatedAsync(_bootstrapServers, numPartitions: 3, replicationFactor: 1);
    }

    public async Task DisposeAsync()
    {
        await _kafkaContainer.DisposeAsync();
    }

    [Fact]
    public async Task PublishTradeAsync_WithSameProductIdAcrossDifferentOrders_ShouldRouteToIdenticalPartitionWithSequentialOffsets()
    {
        // Arrange: 5 distinct orders trading the EXACT SAME financial asset (ProductId)
        using var producer = new KafkaTradeStreamProducer(_bootstrapServers, NullLogger<KafkaTradeStreamProducer>.Instance);
        var sharedProductId = Guid.NewGuid();
        var prices = new[] { 100.00m, 105.50m, 102.25m, 110.00m, 115.75m };

        var deliveryResults = new List<KafkaStreamDeliveryResult>();

        // Act: Publish all 5 trades sequentially
        foreach (var price in prices)
        {
            var tradeEvent = new MarketTradeStreamEvent(
                EventId: Guid.NewGuid(),
                OrderId: Guid.NewGuid(), // Different OrderId every time!
                ProductId: sharedProductId,
                Sku: "SKU-STEEL-001",
                UnitPrice: price,
                Currency: "USD",
                Quantity: 10,
                ExecutedAt: DateTimeOffset.UtcNow);

            var result = await producer.PublishTradeAsync(tradeEvent);
            deliveryResults.Add(result);
        }

        // Assert: All 5 trades MUST land in the exact same partition with strictly increasing offsets
        deliveryResults.Should().HaveCount(5);

        var targetPartition = deliveryResults[0].Partition;
        targetPartition.Should().BeInRange(0, KafkaTopology.DefaultPartitionCount - 1);

        deliveryResults.Select(r => r.Partition)
            .Should().AllBeEquivalentTo(targetPartition,
                "because using ProductId as Partition Key guarantees hash(ProductId) % N always selects the same partition");

        deliveryResults.Select(r => r.Offset)
            .Should().BeInAscendingOrder()
            .And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task KafkaConsumerBackgroundService_WhenTradesArePublished_ShouldConsumeInExactChronologicalOrderPerProduct()
    {
        // Arrange
        var consumerGroup = $"test-analytics-group-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKafkaStreaming(_bootstrapServers, enableConsumerBackgroundWorker: true, consumerGroupId: consumerGroup);

        await using var serviceProvider = services.BuildServiceProvider();
        var producer = serviceProvider.GetRequiredService<IKafkaTradeStreamProducer>();
        var analyticsHandler = serviceProvider.GetRequiredService<MarketTradeAnalyticsHandler>();
        var consumerWorker = serviceProvider.GetServices<IHostedService>()
            .OfType<KafkaTradeStreamConsumerBackgroundService>()
            .Single();

        await consumerWorker.StartAsync(CancellationToken.None);

        try
        {
            var productId = Guid.NewGuid();
            var expectedPrices = new[] { 250.00m, 275.00m, 260.00m, 310.00m };

            // Act: Publish 4 price movements for the same product across different orders
            foreach (var price in expectedPrices)
            {
                await producer.PublishTradeAsync(new MarketTradeStreamEvent(
                    EventId: Guid.NewGuid(),
                    OrderId: Guid.NewGuid(),
                    ProductId: productId,
                    Sku: "SKU-COPPER-002",
                    UnitPrice: price,
                    Currency: "USD",
                    Quantity: 5,
                    ExecutedAt: DateTimeOffset.UtcNow));
            }

            // Wait for the Kafka consumer worker to process all 4 records
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            IReadOnlyList<MarketTradeStreamEvent> processed = [];

            while (DateTimeOffset.UtcNow < deadline)
            {
                processed = analyticsHandler.GetProcessedHistory(productId);
                if (processed.Count >= expectedPrices.Length)
                {
                    break;
                }

                await Task.Delay(100);
            }

            // Assert: Exact chronological sequence is preserved
            processed.Should().HaveCount(4);
            processed.Select(e => e.UnitPrice).Should().ContainInOrder(expectedPrices);
        }
        finally
        {
            await consumerWorker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task KafkaConsumerBackgroundService_WhenPoisonPillArrivesBetweenValidTrades_ShouldRouteToDeadLetterTopicWithoutBlockingPartition()
    {
        // Arrange: Wire up Kafka consumer worker
        var consumerGroup = $"test-poison-pill-group-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKafkaStreaming(_bootstrapServers, enableConsumerBackgroundWorker: true, consumerGroupId: consumerGroup);

        await using var serviceProvider = services.BuildServiceProvider();
        var producer = serviceProvider.GetRequiredService<IKafkaTradeStreamProducer>();
        var analyticsHandler = serviceProvider.GetRequiredService<MarketTradeAnalyticsHandler>();
        var consumerWorker = serviceProvider.GetServices<IHostedService>()
            .OfType<KafkaTradeStreamConsumerBackgroundService>()
            .Single();

        await consumerWorker.StartAsync(CancellationToken.None);

        try
        {
            var productId = Guid.NewGuid();
            var partitionKey = productId.ToString();

            // Act:
            // 1. Publish Valid Trade #1 ($100)
            await producer.PublishTradeAsync(new MarketTradeStreamEvent(
                EventId: Guid.NewGuid(),
                OrderId: Guid.NewGuid(),
                ProductId: productId,
                Sku: "SKU-LITHIUM-99",
                UnitPrice: 100.00m,
                Currency: "USD",
                Quantity: 1,
                ExecutedAt: DateTimeOffset.UtcNow));

            // 2. Publish a Poison Pill (corrupted JSON) with the SAME Partition Key so it lands right behind Trade #1 in the SAME partition!
            const string corruptedPayload = "{ \"corrupted_json\": true, INVALID_SYNTAX }";
            var poisonDelivery = await producer.PublishRawAsync(
                KafkaTopology.TradesTopic,
                partitionKey,
                corruptedPayload);

            // 3. Publish Valid Trade #2 ($200) right behind the Poison Pill in the SAME partition!
            await producer.PublishTradeAsync(new MarketTradeStreamEvent(
                EventId: Guid.NewGuid(),
                OrderId: Guid.NewGuid(),
                ProductId: productId,
                Sku: "SKU-LITHIUM-99",
                UnitPrice: 200.00m,
                Currency: "USD",
                Quantity: 2,
                ExecutedAt: DateTimeOffset.UtcNow));

            // Wait for Valid Trade #1 and Valid Trade #2 to both be processed (proving the partition did NOT stall on the poison pill!)
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            IReadOnlyList<MarketTradeStreamEvent> processed = [];

            while (DateTimeOffset.UtcNow < deadline)
            {
                processed = analyticsHandler.GetProcessedHistory(productId);
                if (processed.Count >= 2)
                {
                    break;
                }

                await Task.Delay(100);
            }

            // Assert 1: Both valid trades before and after the poison pill were processed in order!
            processed.Should().HaveCount(2);
            processed.Select(x => x.UnitPrice).Should().ContainInOrder(100.00m, 200.00m);

            // Assert 2: Verify the Poison Pill was routed to the Dead-Letter Topic (tradeflow.market.trades.dlq) with diagnostic headers
            var dltConfig = new ConsumerConfig
            {
                BootstrapServers = _bootstrapServers,
                GroupId = $"dlt-verifier-{Guid.NewGuid():N}",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = true
            };

            using var dltConsumer = new ConsumerBuilder<string, string>(dltConfig).Build();
            dltConsumer.Subscribe(KafkaTopology.TradesDeadLetterTopic);

            ConsumeResult<string, string>? dltRecord = null;
            var dltDeadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (DateTimeOffset.UtcNow < dltDeadline)
            {
                var candidate = dltConsumer.Consume(TimeSpan.FromMilliseconds(250));
                if (candidate is not null && candidate.Message.Key == partitionKey)
                {
                    dltRecord = candidate;
                    break;
                }
            }

            dltRecord.Should().NotBeNull("because the poison pill must be forwarded to the Dead-Letter Topic");
            dltRecord!.Message.Value.Should().Be(corruptedPayload);

            var originalOffsetHeader = Encoding.UTF8.GetString(dltRecord.Message.Headers.GetLastBytes("x-original-offset"));
            originalOffsetHeader.Should().Be(poisonDelivery.Offset.ToString());
        }
        finally
        {
            await consumerWorker.StopAsync(CancellationToken.None);
        }
    }
}
