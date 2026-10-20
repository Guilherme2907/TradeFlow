using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Orders;
using TradeFlow.Domain.Orders.Events;
using TradeFlow.Infrastructure.Messaging.RabbitMq;
using TradeFlow.Infrastructure.Modules.Orders.Persistence;
using TradeFlow.Infrastructure.Modules.Orders.Persistence.Outbox;
using Xunit;

namespace TradeFlow.Infrastructure.IntegrationTests.Messaging;

/// <summary>
/// ADR-0011: End-to-end integration tests verifying PostgreSQL Transactional Outbox + real RabbitMQ broker:
/// - Topic Exchange routing (exact key vs. wildcard binding)
/// - Publisher Confirms (Basic.Ack before marking ProcessedAt)
/// - Dead-Letter Exchange (DLX) &amp; Dead-Letter Queue (DLQ) routing when a consumer sends Basic.Nack(requeue: false).
/// </summary>
[Trait("Category", "RequiresDocker")]
public class RabbitMqOutboxIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tradeflow_rmq_outbox_test")
        .WithUsername("test_user")
        .WithPassword("test_pass")
        .Build();

    private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder("rabbitmq:4.0-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    private RabbitMqConnectionManager _connectionManager = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            _postgresContainer.StartAsync(),
            _rabbitMqContainer.StartAsync());

        await using var bootstrapContext = CreateOrdersDbContext();
        await bootstrapContext.Database.EnsureCreatedAsync();

        _connectionManager = new RabbitMqConnectionManager(_rabbitMqContainer.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await _connectionManager.DisposeAsync();
        await Task.WhenAll(
            _postgresContainer.DisposeAsync().AsTask(),
            _rabbitMqContainer.DisposeAsync().AsTask());
    }

    private OrdersDbContext CreateOrdersDbContext()
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql(_postgresContainer.GetConnectionString())
            .AddInterceptors(new DomainEventsToOutboxMessagesInterceptor())
            .Options;

        return new OrdersDbContext(options);
    }

    [Fact]
    public async Task OrderSubmission_WhenProcessedByOutbox_ShouldRouteMessagesViaTopicExchangeToBoundQueuesWithPublisherConfirms()
    {
        // Arrange: Declare two consumer queues on the Topic Exchange:
        // 1. "payments.order-submitted" bound specifically to "orders.OrderSubmittedEvent"
        // 2. "audit.all-orders" bound with wildcard "orders.*"
        const string paymentsQueue = "payments.order-submitted";
        const string auditQueue = "audit.all-orders";

        var connection = await _connectionManager.GetConnectionAsync();
        await using var setupChannel = await connection.CreateChannelAsync();

        await RabbitMqTopology.DeclareQueueWithDeadLetteringAsync(
            setupChannel,
            queueName: paymentsQueue,
            routingKeyPattern: "orders.OrderSubmittedEvent");

        await RabbitMqTopology.DeclareQueueWithDeadLetteringAsync(
            setupChannel,
            queueName: auditQueue,
            routingKeyPattern: "orders.*");

        // Create and submit an order in PostgreSQL (generates 3 outbox events)
        Order order;
        await using (var dbContext = CreateOrdersDbContext())
        {
            order = Order.Create(CustomerId.New(), "USD");
            order.AddLine(ProductId.New(), "SKU-COMPRESSOR-01", "Industrial Compressor", Money.Create(750m, "USD"), 2);
            order.Submit();

            await dbContext.Orders.AddAsync(order);
            await dbContext.SaveChangesAsync();
        }

        // Act: Run OutboxProcessor using the real RabbitMqOutboxMessagePublisher
        await using (var workerDbContext = CreateOrdersDbContext())
        await using (var publisher = new RabbitMqOutboxMessagePublisher(_connectionManager, NullLogger<RabbitMqOutboxMessagePublisher>.Instance))
        {
            var processor = new OutboxProcessor(workerDbContext, publisher, NullLogger<OutboxProcessor>.Instance);
            var processedCount = await processor.ProcessBatchAsync();

            processedCount.Should().Be(3);
        }

        // Assert 1: All outbox records in PostgreSQL are marked ProcessedAt != null after Publisher Confirm
        Guid submittedOutboxId;
        await using (var verifyDbContext = CreateOrdersDbContext())
        {
            var outboxMessages = await verifyDbContext.OutboxMessages.ToListAsync();
            outboxMessages.Should().HaveCount(3);
            outboxMessages.Should().OnlyContain(m => m.ProcessedAt != null && m.RetryCount == 0 && m.Error == null);

            submittedOutboxId = outboxMessages.Single(m => m.EventType == nameof(OrderSubmittedEvent)).Id;
        }

        // Assert 2: "payments.order-submitted" queue received ONLY the OrderSubmittedEvent
        var paymentDelivery = await setupChannel.BasicGetAsync(paymentsQueue, autoAck: true);
        paymentDelivery.Should().NotBeNull();
        paymentDelivery!.BasicProperties.MessageId.Should().Be(submittedOutboxId.ToString());
        paymentDelivery.BasicProperties.Type.Should().Be(nameof(OrderSubmittedEvent));

        var paymentPayload = Encoding.UTF8.GetString(paymentDelivery.Body.ToArray());
        using var jsonDoc = JsonDocument.Parse(paymentPayload);
        jsonDoc.RootElement.GetProperty("orderId").GetProperty("value").GetString()
            .Should().Be(order.Id.Value.ToString());

        // No second message should exist in the payments queue
        var extraPaymentDelivery = await setupChannel.BasicGetAsync(paymentsQueue, autoAck: true);
        extraPaymentDelivery.Should().BeNull("payments queue is only bound to orders.OrderSubmittedEvent");

        // Assert 3: "audit.all-orders" queue (bound to "orders.*") received all 3 events
        var auditEvents = new List<string?>();
        for (var i = 0; i < 3; i++)
        {
            var delivery = await setupChannel.BasicGetAsync(auditQueue, autoAck: true);
            delivery.Should().NotBeNull();
            auditEvents.Add(delivery!.BasicProperties.Type);
        }

        auditEvents.Should().BeEquivalentTo([
            nameof(OrderCreatedEvent),
            nameof(OrderLineAddedEvent),
            nameof(OrderSubmittedEvent)]);
    }

    [Fact]
    public async Task ConsumerNackWithoutRequeue_WhenMessageIsRejected_ShouldRouteMessageToDeadLetterQueue()
    {
        // Arrange: Declare a consumer queue with Dead-Lettering configured
        const string consumerQueue = "shipping.order-submitted";
        const string expectedDlqName = "shipping.order-submitted.dlq";

        var connection = await _connectionManager.GetConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await RabbitMqTopology.DeclareQueueWithDeadLetteringAsync(
            channel,
            queueName: consumerQueue,
            routingKeyPattern: "orders.OrderSubmittedEvent");

        var messageId = Guid.NewGuid();
        await using (var publisher = new RabbitMqOutboxMessagePublisher(_connectionManager, NullLogger<RabbitMqOutboxMessagePublisher>.Instance))
        {
            await publisher.PublishAsync(
                messageId,
                nameof(OrderSubmittedEvent),
                "{\"orderId\":\"test-dlq\"}",
                DateTimeOffset.UtcNow);
        }

        // Act: Consumer reads message with autoAck: false and rejects it with BasicNackAsync(requeue: false)
        var delivery = await channel.BasicGetAsync(consumerQueue, autoAck: false);
        delivery.Should().NotBeNull();

        await channel.BasicNackAsync(
            deliveryTag: delivery!.DeliveryTag,
            multiple: false,
            requeue: false);

        // Assert: RabbitMQ Dead-Letter Exchange (DLX) automatically routes the rejected message to the DLQ
        var dlqDelivery = await channel.BasicGetAsync(expectedDlqName, autoAck: true);
        dlqDelivery.Should().NotBeNull("Rejected message with requeue: false must be routed to the Dead-Letter Queue");
        dlqDelivery!.BasicProperties.MessageId.Should().Be(messageId.ToString());
        Encoding.UTF8.GetString(dlqDelivery.Body.ToArray()).Should().Contain("test-dlq");
    }
}
