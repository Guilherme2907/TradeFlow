using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Testcontainers.PostgreSql;
using TradeFlow.Application.Common.Models;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Inventory;
using TradeFlow.Domain.Orders;
using TradeFlow.Infrastructure.Modules.Inventory.Persistence;
using TradeFlow.Infrastructure.Modules.Orders.Persistence;
using TradeFlow.Infrastructure.Persistence;
using Xunit;

namespace TradeFlow.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// ADR-0010: verifies the module-agnostic UnitOfWork keeps a single transaction across module contexts
/// and that a retried delegate does not observe mutated in-memory state from the failed attempt.
/// </summary>
[Trait("Category", "RequiresDocker")]
public class UnitOfWorkTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tradeflow_uow_test")
        .WithUsername("test_user")
        .WithPassword("test_pass")
        .Build();

    private readonly TransientFailureOnceInterceptor _interceptor = new();

    private NpgsqlConnection _connection = null!;
    private OrdersDbContext _ordersContext = null!;
    private InventoryDbContext _inventoryContext = null!;
    private UnitOfWork _unitOfWork = null!;

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        // Schema bootstrap on a throwaway connection
        var bootstrapOptions = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString()).Options;
        await using (var bootstrapOrders = new OrdersDbContext(bootstrapOptions))
        {
            await bootstrapOrders.Database.EnsureCreatedAsync();
        }

        var inventoryBootstrapOptions = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString()).Options;
        await using (var bootstrapInventory = new InventoryDbContext(inventoryBootstrapOptions))
        {
            await bootstrapInventory.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS inventory;");
            var creator = (RelationalDatabaseCreator)bootstrapInventory.Database.GetService<IDatabaseCreator>();
            await creator.CreateTablesAsync();
        }

        // Runtime wiring: both module contexts share ONE connection, with retry enabled (as in production)
        _connection = new NpgsqlConnection(_dbContainer.GetConnectionString());

        _ordersContext = new OrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql(_connection, o => o.EnableRetryOnFailure(3, TimeSpan.FromMilliseconds(100), null))
            .Options);

        _inventoryContext = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(_connection, o => o.EnableRetryOnFailure(3, TimeSpan.FromMilliseconds(100), null))
            .AddInterceptors(_interceptor)
            .Options);

        _unitOfWork = new UnitOfWork([_ordersContext, _inventoryContext]);
    }

    public async Task DisposeAsync()
    {
        await _ordersContext.DisposeAsync();
        await _inventoryContext.DisposeAsync();
        await _connection.DisposeAsync();
        await _dbContainer.DisposeAsync();
    }

    private (OrdersDbContext Orders, InventoryDbContext Inventory) CreateFreshContexts()
    {
        var orders = new OrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString()).Options);
        var inventory = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString()).Options);
        return (orders, inventory);
    }

    private async Task<(OrderId OrderId, ProductId ProductId)> SeedOrderAndStockAsync()
    {
        var productId = ProductId.New();
        var order = Order.Create(CustomerId.New(), "USD");
        order.AddLine(productId, "SKU-UOW", "Valve", Money.Create(100m, "USD"), 5);

        var (orders, inventory) = CreateFreshContexts();
        await using (orders)
        await using (inventory)
        {
            orders.Orders.Add(order);
            inventory.InventoryItems.Add(InventoryItem.Create(productId, "SKU-UOW", initialQuantity: 100));
            await orders.SaveChangesAsync();
            await inventory.SaveChangesAsync();
        }

        return (order.Id, productId);
    }

    /// <summary>
    /// Mimics SubmitOrderCommandHandler: loads aggregates inside the delegate, mutates both modules, saves.
    /// </summary>
    private Task<Result> SubmitAndAllocateAsync(OrderId orderId, ProductId productId) =>
        _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var order = (await new OrderRepository(_ordersContext).GetByIdAsync(orderId))!;
            order.Submit();

            var item = (await new InventoryRepository(_inventoryContext).GetByProductIdsForUpdateAsync([productId])).Single();
            item.Allocate(5);

            await _unitOfWork.SaveChangesAsync();
            return Result.Success();
        });

    [Fact]
    public async Task ExecuteInTransactionAsync_WhenOperationSucceeds_ShouldCommitBothModules()
    {
        var (orderId, productId) = await SeedOrderAndStockAsync();
        _interceptor.Disable();

        var result = await SubmitAndAllocateAsync(orderId, productId);

        result.IsSuccess.Should().BeTrue();

        var (orders, inventory) = CreateFreshContexts();
        await using (orders)
        await using (inventory)
        {
            (await orders.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId)).Status.Should().Be(OrderStatus.Submitted);
            (await inventory.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == productId))
                .AllocatedQuantity.Should().Be(5);
        }
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_WhenOperationReturnsFailure_ShouldRollbackBothModules()
    {
        var (orderId, productId) = await SeedOrderAndStockAsync();
        _interceptor.Disable();

        var result = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var order = (await new OrderRepository(_ordersContext).GetByIdAsync(orderId))!;
            order.Submit();
            var item = await _inventoryContext.InventoryItems.SingleAsync(i => i.ProductId == productId);
            item.Allocate(5);
            await _unitOfWork.SaveChangesAsync();

            // Both modules were already flushed inside the transaction; a business failure must undo both.
            return Result.Failure(Error.Conflict("FORCED_FAILURE", "rollback requested"));
        });

        result.IsFailure.Should().BeTrue();

        var (orders, inventory) = CreateFreshContexts();
        await using (orders)
        await using (inventory)
        {
            (await orders.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId)).Status.Should().Be(OrderStatus.Draft);
            (await inventory.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == productId))
                .AllocatedQuantity.Should().Be(0);
        }
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_WhenTransientFailureOccurs_ShouldRetryWithCleanStateAndApplyChangesExactlyOnce()
    {
        var (orderId, productId) = await SeedOrderAndStockAsync();

        // First inventory UPDATE fails transiently AFTER the orders UPDATE already ran inside the transaction.
        // Without tracker reset, retry would throw (order already Submitted in memory) or double-allocate.
        var result = await SubmitAndAllocateAsync(orderId, productId);

        result.IsSuccess.Should().BeTrue();
        _interceptor.FailureCount.Should().Be(1, "exactly one transient failure was injected");

        var (orders, inventory) = CreateFreshContexts();
        await using (orders)
        await using (inventory)
        {
            (await orders.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId)).Status.Should().Be(OrderStatus.Submitted);
            (await inventory.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == productId))
                .AllocatedQuantity.Should().Be(5, "the reservation must be applied exactly once despite the retry");
        }
    }

    private sealed class TransientFailureOnceInterceptor : DbCommandInterceptor
    {
        private bool _enabled = true;

        public int FailureCount { get; private set; }

        public void Disable() => _enabled = false;

        private void MaybeFail(DbCommand command)
        {
            if (_enabled && FailureCount == 0 &&
                command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                FailureCount++;
                throw new NpgsqlException("Simulated transient failure", new IOException("connection reset"));
            }
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            MaybeFail(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            MaybeFail(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
