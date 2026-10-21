using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Orders;
using TradeFlow.Domain.Orders.Events;
using TradeFlow.Infrastructure.Messaging.RabbitMq;
using TradeFlow.Infrastructure.Modules.Orders.Consumers;
using TradeFlow.Infrastructure.Modules.Orders.Persistence;
using TradeFlow.Infrastructure.Modules.Orders.Persistence.Inbox;
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

    [Fact]
    public async Task IdempotentInboxConsumer_WhenSameMessageIsDeliveredTwiceFromRabbitMq_ShouldExecuteSideEffectOnlyOnceAndAckBoth()
    {
        // Arrange: Simulate At-Least-Once duplicate delivery where the exact same MessageId is published twice to RabbitMQ
        const string invoicingQueue = "invoicing.order-submitted";
        const string consumerName = "InvoiceGenerationConsumer";

        var connection = await _connectionManager.GetConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await RabbitMqTopology.DeclareQueueWithDeadLetteringAsync(
            channel,
            queueName: invoicingQueue,
            routingKeyPattern: "orders.OrderSubmittedEvent");

        var duplicateMessageId = Guid.NewGuid();
        await using (var publisher = new RabbitMqOutboxMessagePublisher(_connectionManager, NullLogger<RabbitMqOutboxMessagePublisher>.Instance))
        {
            // Publish #1 (original delivery)
            await publisher.PublishAsync(
                duplicateMessageId,
                nameof(OrderSubmittedEvent),
                "{\"orderId\":\"order-123\"}",
                DateTimeOffset.UtcNow);

            // Publish #2 (simulated duplicate redelivery after worker restart)
            await publisher.PublishAsync(
                duplicateMessageId,
                nameof(OrderSubmittedEvent),
                "{\"orderId\":\"order-123\"}",
                DateTimeOffset.UtcNow);
        }

        var sideEffectExecutionCount = 0;

        // Act 1: Consume first copy of the message
        await using (var dbContext1 = CreateOrdersDbContext())
        {
            var consumer1 = new IdempotentInboxConsumer(dbContext1, NullLogger<IdempotentInboxConsumer>.Instance);
            var firstResult = await consumer1.ConsumeNextFromQueueAsync(
                channel,
                invoicingQueue,
                consumerName,
                (_, _, _, _) =>
                {
                    sideEffectExecutionCount++;
                    return Task.CompletedTask;
                });

            firstResult.Should().BeTrue("First delivery of MessageId must be executed");
        }

        // Act 2: Consume second (duplicate) copy of the message
        await using (var dbContext2 = CreateOrdersDbContext())
        {
            var consumer2 = new IdempotentInboxConsumer(dbContext2, NullLogger<IdempotentInboxConsumer>.Instance);
            var secondResult = await consumer2.ConsumeNextFromQueueAsync(
                channel,
                invoicingQueue,
                consumerName,
                (_, _, _, _) =>
                {
                    sideEffectExecutionCount++;
                    return Task.CompletedTask;
                });

            secondResult.Should().BeFalse("Second delivery of the same MessageId for the same ConsumerName must be skipped via Inbox deduplication");
        }

        // Assert: Business side-effect ran ONLY ONCE, queue is completely drained (both ACKed),
        // and a different consumer can still process the same MessageId independently!
        sideEffectExecutionCount.Should().Be(1);

        var remainingInQueue = await channel.BasicGetAsync(invoicingQueue, autoAck: true);
        remainingInQueue.Should().BeNull("Both deliveries were ACKed and removed from the RabbitMQ queue");

        await using (var verifyContext = CreateOrdersDbContext())
        {
            var inboxEntry = await verifyContext.InboxMessages
                .SingleOrDefaultAsync(x => x.MessageId == duplicateMessageId && x.ConsumerName == consumerName);
            inboxEntry.Should().NotBeNull();

            // Verify composite key isolation: a different consumer name CAN process the same MessageId
            var otherConsumer = new IdempotentInboxConsumer(verifyContext, NullLogger<IdempotentInboxConsumer>.Instance);
            var otherConsumerExecuted = await otherConsumer.ExecuteIdempotentlyAsync(
                duplicateMessageId,
                consumerName: "LoyaltyPointsConsumer",
                eventType: nameof(OrderSubmittedEvent),
                _ => Task.CompletedTask);

            otherConsumerExecuted.Should().BeTrue("Composite primary key (MessageId, ConsumerName) allows distinct consumers to process the same event");
        }
    }

    [Fact]
    public async Task IdempotentInboxConsumer_WhenHandlerThrowsException_ShouldRollbackInboxRecordAndRouteMessageToDlq()
    {
        // Arrange
        const string failingQueue = "tax-reporting.order-submitted";
        const string expectedDlqName = "tax-reporting.order-submitted.dlq";
        const string consumerName = "TaxReportingConsumer";

        var connection = await _connectionManager.GetConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await RabbitMqTopology.DeclareQueueWithDeadLetteringAsync(
            channel,
            queueName: failingQueue,
            routingKeyPattern: "orders.OrderSubmittedEvent");

        var failingMessageId = Guid.NewGuid();
        await using (var publisher = new RabbitMqOutboxMessagePublisher(_connectionManager, NullLogger<RabbitMqOutboxMessagePublisher>.Instance))
        {
            await publisher.PublishAsync(
                failingMessageId,
                nameof(OrderSubmittedEvent),
                "{\"orderId\":\"tax-fail-order\"}",
                DateTimeOffset.UtcNow);
        }

        // Act: Consumer handler throws an exception mid-processing
        await using (var dbContext = CreateOrdersDbContext())
        {
            var consumer = new IdempotentInboxConsumer(dbContext, NullLogger<IdempotentInboxConsumer>.Instance);

            var act = async () => await consumer.ConsumeNextFromQueueAsync(
                channel,
                failingQueue,
                consumerName,
                (_, _, _, _) => throw new InvalidOperationException("External Tax Authority API 503 Unavailable"));

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        // Assert 1: The inbox record MUST have been rolled back in PostgreSQL so the message can be reprocessed later
        await using (var verifyContext = CreateOrdersDbContext())
        {
            var inboxExists = await verifyContext.InboxMessages
                .AnyAsync(x => x.MessageId == failingMessageId && x.ConsumerName == consumerName);

            inboxExists.Should().BeFalse("Failed handler transaction must roll back the INSERT into orders.inbox_messages");
        }

        // Assert 2: The message was NACKed (requeue: false) and landed in the Dead-Letter Queue
        var dlqDelivery = await channel.BasicGetAsync(expectedDlqName, autoAck: true);
        dlqDelivery.Should().NotBeNull();
        dlqDelivery!.BasicProperties.MessageId.Should().Be(failingMessageId.ToString());
    }

    [Fact]
    public async Task RabbitMqConsumerBackgroundService_WhenOrderIsSubmittedAndCancelled_ShouldConsumePushEventsAndRecordInboxEntries()
    {
        // Arrange: Wire up full DI container with PostgreSQL + RabbitMQ + all 3 Order Event Consumers + Push BackgroundService
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNpgsqlModuleDbContexts(_postgresContainer.GetConnectionString());
        services.AddRabbitMqMessaging(_rabbitMqContainer.GetConnectionString(), enableConsumerBackgroundWorker: true);
        services.AddScoped<OutboxProcessor>();
        services.AddScoped<IdempotentInboxConsumer>();

        await using var serviceProvider = services.BuildServiceProvider();
        var consumerWorker = serviceProvider.GetServices<IHostedService>()
            .OfType<RabbitMqConsumerBackgroundService>()
            .Single();

        // Start the push-based consumer BackgroundService
        await consumerWorker.StartAsync(CancellationToken.None);

        try
        {
            // Give the background service a brief moment to declare topology & start BasicConsume
            await Task.Delay(250);

            // Act: Create, add line, submit, and cancel an Order (raises 4 domain events)
            Guid[] raisedEventIds;
            await using (var scope = serviceProvider.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

                var order = Order.Create(CustomerId.New(), "USD");
                order.AddLine(ProductId.New(), "SKU-BOILER-01", "Steam Boiler", Money.Create(2500m, "USD"), 1);
                order.Submit();
                order.Cancel("Customer requested cancellation before dispatch");

                raisedEventIds = order.DomainEvents.Select(e => e.EventId).ToArray();
                raisedEventIds.Should().HaveCount(4);

                await dbContext.Orders.AddAsync(order);
                await dbContext.SaveChangesAsync();
            }

            // Publish the 4 outbox events to RabbitMQ
            await using (var publishScope = serviceProvider.CreateAsyncScope())
            {
                var processor = publishScope.ServiceProvider.GetRequiredService<OutboxProcessor>();
                var published = await processor.ProcessBatchAsync();
                published.Should().Be(4);
            }

            // Wait for RabbitMqConsumerBackgroundService to push-consume all 6 deliveries:
            // - 1 for OrderSubmittedInvoiceConsumer (orders.OrderSubmittedEvent)
            // - 1 for OrderCancelledNotificationConsumer (orders.OrderCancelledEvent)
            // - 4 for OrderLifecycleAuditConsumer (orders.* wildcard)
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            List<InboxMessage> matchedInboxEntries = [];

            while (DateTimeOffset.UtcNow < deadline)
            {
                await using var checkContext = CreateOrdersDbContext();
                matchedInboxEntries = await checkContext.InboxMessages
                    .Where(m => raisedEventIds.Contains(m.MessageId))
                    .ToListAsync();

                if (matchedInboxEntries.Count >= 6)
                {
                    break;
                }

                await Task.Delay(100);
            }

            // Assert: All 3 registered consumers received and processed their subscribed events via Push
            matchedInboxEntries.Should().HaveCount(6);

            matchedInboxEntries
                .Where(m => m.ConsumerName == nameof(OrderSubmittedInvoiceConsumer))
                .Select(m => m.EventType)
                .Should().ContainSingle().Which.Should().Be(nameof(OrderSubmittedEvent));

            matchedInboxEntries
                .Where(m => m.ConsumerName == nameof(OrderCancelledNotificationConsumer))
                .Select(m => m.EventType)
                .Should().ContainSingle().Which.Should().Be(nameof(OrderCancelledEvent));

            matchedInboxEntries
                .Where(m => m.ConsumerName == nameof(OrderLifecycleAuditConsumer))
                .Select(m => m.EventType)
                .Should().BeEquivalentTo([
                    nameof(OrderCreatedEvent),
                    nameof(OrderLineAddedEvent),
                    nameof(OrderSubmittedEvent),
                    nameof(OrderCancelledEvent)]);
        }
        finally
        {
            await consumerWorker.StopAsync(CancellationToken.None);
        }
    }
}
