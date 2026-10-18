using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Orders;
using TradeFlow.Domain.Orders.Events;
using TradeFlow.Infrastructure.Messaging;
using TradeFlow.Infrastructure.Modules.Orders.Persistence;
using TradeFlow.Infrastructure.Modules.Orders.Persistence.Outbox;
using Xunit;

namespace TradeFlow.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// ADR-0011: Verifies OutboxProcessor polling mechanics against real PostgreSQL,
/// including batch processing, poison message isolation (RetryCount &lt; 5),
/// and concurrent multi-worker isolation via SELECT ... FOR UPDATE SKIP LOCKED.
/// </summary>
[Trait("Category", "RequiresDocker")]
public class OutboxProcessorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tradeflow_outbox_processor_test")
        .WithUsername("test_user")
        .WithPassword("test_pass")
        .Build();

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        await using var bootstrapContext = CreateOrdersDbContext();
        await bootstrapContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContainer.DisposeAsync();
    }

    private OrdersDbContext CreateOrdersDbContext()
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString())
            .AddInterceptors(new DomainEventsToOutboxMessagesInterceptor())
            .Options;

        return new OrdersDbContext(options);
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenUnprocessedMessagesExist_ShouldPublishAllAndMarkProcessedInPostgreSql()
    {
        // Arrange: Create and submit an order (produces OrderCreatedEvent, OrderLineAddedEvent, OrderSubmittedEvent)
        await using (var seedContext = CreateOrdersDbContext())
        {
            var order = Order.Create(CustomerId.New(), "USD");
            order.AddLine(ProductId.New(), "SKU-TURBINE-01", "Gas Turbine", Money.Create(1200m, "USD"), 1);
            order.Submit();

            await seedContext.Orders.AddAsync(order);
            await seedContext.SaveChangesAsync();
        }

        var publishedIds = new List<(Guid Id, string EventType)>();
        var fakePublisher = new DelegateOutboxPublisher((id, eventType, _, _, _) =>
        {
            publishedIds.Add((id, eventType));
            return Task.CompletedTask;
        });

        // Act: Process outbox batch
        await using (var workerContext = CreateOrdersDbContext())
        {
            var processor = new OutboxProcessor(workerContext, fakePublisher, NullLogger<OutboxProcessor>.Instance);
            var processedCount = await processor.ProcessBatchAsync();

            processedCount.Should().Be(3);
        }

        // Assert: All 3 events were published in chronological order and marked as processed in PostgreSQL
        publishedIds.Should().HaveCount(3);
        publishedIds.Select(x => x.EventType).Should().ContainInOrder(
            nameof(OrderCreatedEvent),
            nameof(OrderLineAddedEvent),
            nameof(OrderSubmittedEvent));

        await using (var assertContext = CreateOrdersDbContext())
        {
            var messages = await assertContext.OutboxMessages.ToListAsync();
            messages.Should().HaveCount(3);
            messages.Should().OnlyContain(m => m.ProcessedAt != null && m.Error == null && m.RetryCount == 0);

            // Second poll should find 0 unprocessed messages
            var processor = new OutboxProcessor(assertContext, fakePublisher, NullLogger<OutboxProcessor>.Instance);
            var secondCount = await processor.ProcessBatchAsync();
            secondCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenPublisherThrows_ShouldIncrementRetryCountAndIsolatePoisonMessageAfterMaxRetries()
    {
        // Arrange: Insert a single outbox message directly
        var poisonMessageId = Guid.NewGuid();
        await using (var seedContext = CreateOrdersDbContext())
        {
            await seedContext.OutboxMessages.AddAsync(new OutboxMessage(
                poisonMessageId,
                nameof(OrderSubmittedEvent),
                "{\"corrupted\":true}",
                DateTimeOffset.UtcNow));
            await seedContext.SaveChangesAsync();
        }

        var attemptCalls = 0;
        var failingPublisher = new DelegateOutboxPublisher((_, _, _, _, _) =>
        {
            attemptCalls++;
            throw new InvalidOperationException("RabbitMQ NACK: Exchange unavailable");
        });

        // Act: Poll MaxRetryCount (5) times
        for (var i = 1; i <= OutboxProcessor.MaxRetryCount; i++)
        {
            await using var workerContext = CreateOrdersDbContext();
            var processor = new OutboxProcessor(workerContext, failingPublisher, NullLogger<OutboxProcessor>.Instance);

            var count = await processor.ProcessBatchAsync();
            count.Should().Be(1);
        }

        attemptCalls.Should().Be(OutboxProcessor.MaxRetryCount);

        // Assert: After 5 failures, the message has RetryCount == 5, Error recorded, ProcessedAt == null
        await using (var verifyContext = CreateOrdersDbContext())
        {
            var msg = await verifyContext.OutboxMessages.SingleAsync(m => m.Id == poisonMessageId);
            msg.ProcessedAt.Should().BeNull();
            msg.RetryCount.Should().Be(OutboxProcessor.MaxRetryCount);
            msg.Error.Should().Contain("RabbitMQ NACK");

            // 6th poll MUST skip the poison message (Head-of-Line Blocking prevention)
            var processor = new OutboxProcessor(verifyContext, failingPublisher, NullLogger<OutboxProcessor>.Instance);
            var sixthPollCount = await processor.ProcessBatchAsync();

            sixthPollCount.Should().Be(0, "Poison messages reaching MaxRetryCount must be isolated from polling");
            attemptCalls.Should().Be(OutboxProcessor.MaxRetryCount, "Publisher must not be invoked again for isolated poison message");
        }
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenTwoWorkersPollConcurrently_ShouldSkipLockedRowsAndPreventDuplicatePublishing()
    {
        // Arrange: Seed 4 unprocessed messages in chronological order
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var seededIds = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();

        await using (var seedContext = CreateOrdersDbContext())
        {
            for (var i = 0; i < 4; i++)
            {
                await seedContext.OutboxMessages.AddAsync(new OutboxMessage(
                    seededIds[i],
                    $"Event_{i}",
                    "{}",
                    baseTime.AddSeconds(i)));
            }
            await seedContext.SaveChangesAsync();
        }

        // Synchronization primitives to prove Worker 1 holds locks on rows 0..1 while Worker 2 polls rows 2..3
        var worker1HoldingLocks = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWorker1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var worker1Published = new ConcurrentBag<Guid>();
        var worker2Published = new ConcurrentBag<Guid>();

        var publisher1 = new DelegateOutboxPublisher(async (id, _, _, _, _) =>
        {
            worker1Published.Add(id);
            worker1HoldingLocks.TrySetResult(); // Signal that Worker 1 is inside the transaction holding FOR UPDATE locks
            await releaseWorker1.Task;
        });

        var publisher2 = new DelegateOutboxPublisher((id, _, _, _, _) =>
        {
            worker2Published.Add(id);
            return Task.CompletedTask;
        });

        // Act: Start Worker 1 with batchSize = 2 (locks first 2 rows and pauses)
        var worker1Task = Task.Run(async () =>
        {
            await using var context1 = CreateOrdersDbContext();
            var processor1 = new OutboxProcessor(context1, publisher1, NullLogger<OutboxProcessor>.Instance);
            return await processor1.ProcessBatchAsync(batchSize: 2);
        });

        // Wait until Worker 1 has acquired the PostgreSQL row locks and is mid-publish
        await worker1HoldingLocks.Task;

        // While Worker 1 is paused holding locks on rows 0 and 1, Worker 2 polls with batchSize = 2
        int worker2Count;
        await using (var context2 = CreateOrdersDbContext())
        {
            var processor2 = new OutboxProcessor(context2, publisher2, NullLogger<OutboxProcessor>.Instance);
            worker2Count = await processor2.ProcessBatchAsync(batchSize: 2);
        }

        // Release Worker 1 so it can finish publishing and commit
        releaseWorker1.SetResult();
        var worker1Count = await worker1Task;

        // Assert: Both workers processed 2 messages each without blocking or duplicating any message
        worker1Count.Should().Be(2);
        worker2Count.Should().Be(2);

        worker1Published.Should().BeEquivalentTo([seededIds[0], seededIds[1]]);
        worker2Published.Should().BeEquivalentTo([seededIds[2], seededIds[3]]);
        worker1Published.Intersect(worker2Published).Should().BeEmpty("SKIP LOCKED must guarantee disjoint message batches across concurrent workers");
    }

    private sealed class DelegateOutboxPublisher : IOutboxMessagePublisher
    {
        private readonly Func<Guid, string, string, DateTimeOffset, CancellationToken, Task> _handler;

        public DelegateOutboxPublisher(Func<Guid, string, string, DateTimeOffset, CancellationToken, Task> handler)
        {
            _handler = handler;
        }

        public Task PublishAsync(
            Guid messageId,
            string eventType,
            string payload,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken = default)
            => _handler(messageId, eventType, payload, occurredAt, cancellationToken);
    }
}
