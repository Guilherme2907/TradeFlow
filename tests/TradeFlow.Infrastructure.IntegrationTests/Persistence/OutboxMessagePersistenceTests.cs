using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Orders;
using TradeFlow.Domain.Orders.Events;
using TradeFlow.Infrastructure.Modules.Orders.Persistence;
using TradeFlow.Infrastructure.Modules.Orders.Persistence.Outbox;
using Xunit;

namespace TradeFlow.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// ADR-0011: Verifies the Transactional Outbox pattern guarantees atomic persistence
/// of domain events alongside aggregate mutations in PostgreSQL, avoiding the Dual-Write problem.
/// </summary>
[Trait("Category", "RequiresDocker")]
public class OutboxMessagePersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tradeflow_outbox_test")
        .WithUsername("test_user")
        .WithPassword("test_pass")
        .Build();

    private OrdersDbContext _context = null!;

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString())
            .AddInterceptors(new DomainEventsToOutboxMessagesInterceptor())
            .Options;

        _context = new OrdersDbContext(options);
        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _dbContainer.DisposeAsync();
    }

    [Fact]
    public async Task SubmitOrder_WhenSaved_ShouldAtomicallyPersistOrderAndOutboxMessageInPostgreSql()
    {
        // Arrange
        var customerId = CustomerId.New();
        var order = Order.Create(customerId, "USD");
        var productId = ProductId.New();
        order.AddLine(productId, "SKU-PUMP-01", "Hydraulic Pump", Money.Create(250m, "USD"), 2);
        order.Submit();

        // Act
        await _context.Orders.AddAsync(order);
        await _context.SaveChangesAsync();

        // Assert in-memory state: DomainEvents must have been cleared from the aggregate
        order.DomainEvents.Should().BeEmpty();

        // Clear EF Core change tracker to force a physical read from PostgreSQL
        _context.ChangeTracker.Clear();

        // Verify Order persisted
        var persistedOrder = await _context.Orders.FindAsync(order.Id);
        persistedOrder.Should().NotBeNull();
        persistedOrder!.Status.Should().Be(OrderStatus.Submitted);

        // Verify OutboxMessage persisted in orders.outbox_messages table
        var outboxMessages = await _context.OutboxMessages.ToListAsync();
        outboxMessages.Should().NotBeEmpty();

        var submittedMessage = outboxMessages.SingleOrDefault(m => m.EventType == nameof(OrderSubmittedEvent));
        submittedMessage.Should().NotBeNull();
        submittedMessage!.ProcessedAt.Should().BeNull();
        submittedMessage.Error.Should().BeNull();
        submittedMessage.RetryCount.Should().Be(0);
        submittedMessage.OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));

        // Verify payload contains valid JSON with OrderId and TotalAmount
        using var jsonDoc = JsonDocument.Parse(submittedMessage.Payload);
        var root = jsonDoc.RootElement;
        root.TryGetProperty("orderId", out var orderIdProp).Should().BeTrue();
        orderIdProp.GetProperty("value").GetString().Should().Be(order.Id.Value.ToString());

        root.TryGetProperty("totalAmount", out var totalAmountProp).Should().BeTrue();
        totalAmountProp.GetProperty("amount").GetDecimal().Should().Be(500m);
        totalAmountProp.GetProperty("currency").GetString().Should().Be("USD");
    }

    [Fact]
    public async Task SubmitOrder_WhenTransactionRollsBack_ShouldNotPersistOrderOrOutboxMessage()
    {
        // Arrange: Start an explicit transaction to test Dual-Write atomicity guarantee
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var order = Order.Create(CustomerId.New(), "USD");
        order.AddLine(ProductId.New(), "SKU-VALVE-99", "Check Valve", Money.Create(100m, "USD"), 1);
        order.Submit();

        await _context.Orders.AddAsync(order);
        await _context.SaveChangesAsync();

        // Act: Simulate transaction rollback due to downstream failure or conflict
        await transaction.RollbackAsync();

        _context.ChangeTracker.Clear();

        // Assert: Neither order nor outbox messages must exist in PostgreSQL
        var orderExists = await _context.Orders.AnyAsync(o => o.Id == order.Id);
        var hasOutboxMessages = await _context.OutboxMessages.AnyAsync();

        orderExists.Should().BeFalse("Order must not be saved if transaction rolls back");
        hasOutboxMessages.Should().BeFalse("Outbox message must not exist if aggregate mutation rolls back (no phantom events)");
    }

    [Fact]
    public void OutboxMessage_MarkAsProcessed_ShouldUpdateProcessedAtAndClearError()
    {
        // Arrange
        var message = new OutboxMessage(Guid.NewGuid(), "TestEvent", "{}", DateTimeOffset.UtcNow);
        message.RecordFailure("Network partition");

        // Act
        var processedAt = DateTimeOffset.UtcNow;
        message.MarkAsProcessed(processedAt);

        // Assert
        message.ProcessedAt.Should().Be(processedAt);
        message.Error.Should().BeNull();
        message.RetryCount.Should().Be(1);
    }

    [Fact]
    public void OutboxMessage_RecordFailure_ShouldIncrementRetryCountAndSetError()
    {
        // Arrange
        var message = new OutboxMessage(Guid.NewGuid(), "TestEvent", "{}", DateTimeOffset.UtcNow);

        // Act
        message.RecordFailure("Connection refused");
        message.RecordFailure("Timeout waiting for ACK");

        // Assert
        message.RetryCount.Should().Be(2);
        message.Error.Should().Be("Timeout waiting for ACK");
        message.ProcessedAt.Should().BeNull();
    }
}
