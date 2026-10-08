using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Orders;
using TradeFlow.Infrastructure.Modules.Orders.Persistence;
using Xunit;

namespace TradeFlow.Infrastructure.IntegrationTests.Persistence;

[Trait("Category", "RequiresDocker")]
public class OrderRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tradeflow_test")
        .WithUsername("test_user")
        .WithPassword("test_pass")
        .Build();

    private OrdersDbContext _context = null!;
    private OrderRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString())
            .Options;

        _context = new OrdersDbContext(options);
        await _context.Database.EnsureCreatedAsync();

        _repository = new OrderRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _dbContainer.DisposeAsync();
    }

    [Fact]
    public async Task AddAsync_ShouldPersistOrderWithLinesAndComplexMoneyProperty()
    {
        // Arrange
        var customerId = CustomerId.New();
        var minimumOrderValue = Money.Create(500m, "USD");
        var order = Order.Create(customerId, "USD", minimumOrderValue);

        var productId1 = ProductId.New();
        var productId2 = ProductId.New();

        order.AddLine(productId1, "SKU-VALVE-01", "Industrial Valve", Money.Create(150m, "USD"), 2);
        order.AddLine(productId2, "SKU-PIPE-10", "Steel Pipe", Money.Create(250m, "USD"), 1);

        // Act: Save to real PostgreSQL database
        await _repository.AddAsync(order);
        await _context.SaveChangesAsync();

        // Clear change tracker to guarantee query hits PostgreSQL, not EF memory cache
        _context.ChangeTracker.Clear();

        // Retrieve from database
        var loadedOrder = await _repository.GetByIdAsync(order.Id);

        // Assert
        loadedOrder.Should().NotBeNull();
        loadedOrder!.Id.Should().Be(order.Id);
        loadedOrder.CustomerId.Should().Be(customerId);
        loadedOrder.Currency.Should().Be("USD");
        loadedOrder.Status.Should().Be(OrderStatus.Draft);

        // Verify complex property mapping (Amount & Currency)
        loadedOrder.MinimumOrderValue.Should().NotBeNull();
        loadedOrder.MinimumOrderValue!.Value.Amount.Should().Be(500m);
        loadedOrder.MinimumOrderValue!.Value.Currency.Should().Be("USD");

        // Verify backing field mapping (_lines populated via EF Core PropertyAccessMode.Field)
        loadedOrder.Lines.Should().HaveCount(2);

        var line1 = loadedOrder.Lines.First(l => l.ProductId == productId1);
        line1.Sku.Should().Be("SKU-VALVE-01");
        line1.ProductName.Should().Be("Industrial Valve");
        line1.UnitPrice.Amount.Should().Be(150m);
        line1.UnitPrice.Currency.Should().Be("USD");
        line1.Quantity.Should().Be(2);
        line1.LineTotal.Should().Be(Money.Create(300m, "USD"));

        var line2 = loadedOrder.Lines.First(l => l.ProductId == productId2);
        line2.Sku.Should().Be("SKU-PIPE-10");
        line2.Quantity.Should().Be(1);
        line2.LineTotal.Should().Be(Money.Create(250m, "USD"));

        loadedOrder.TotalAmount.Should().Be(Money.Create(550m, "USD"));
    }

    [Fact]
    public async Task Concurrency_WhenTwoTransactionsModifySameOrder_ShouldThrowDbUpdateConcurrencyException()
    {
        // Arrange
        var customerId = CustomerId.New();
        var order = Order.Create(customerId, "USD");
        order.AddLine(ProductId.New(), "SKU-VALVE-01", "Valve", Money.Create(100m, "USD"), 1);

        await _repository.AddAsync(order);
        await _context.SaveChangesAsync();

        // Create two separate DbContext instances simulating two parallel HTTP requests
        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString())
            .Options;

        await using var contextA = new OrdersDbContext(options);
        await using var contextB = new OrdersDbContext(options);

        var orderRepoA = new OrderRepository(contextA);
        var orderRepoB = new OrderRepository(contextB);

        var orderA = await orderRepoA.GetByIdAsync(order.Id);
        var orderB = await orderRepoB.GetByIdAsync(order.Id);

        // Act: User A modifies the order by adding a line (which touches Order.UpdatedAt) and saves first
        orderA!.AddLine(ProductId.New(), "SKU-PIPE-02", "Pipe", Money.Create(50m, "USD"), 2);
        await contextA.SaveChangesAsync();

        // User B attempts to submit the order based on stale version
        orderB!.Submit();

        // Assert: EF Core Optimistic Concurrency Control catches conflict
        var actB = async () => await contextB.SaveChangesAsync();
        await actB.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
