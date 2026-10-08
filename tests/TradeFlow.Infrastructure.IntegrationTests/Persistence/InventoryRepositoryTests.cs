using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Inventory;
using TradeFlow.Infrastructure.Modules.Inventory.Persistence;
using Xunit;

namespace TradeFlow.Infrastructure.IntegrationTests.Persistence;

[Trait("Category", "RequiresDocker")]
public class InventoryRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tradeflow_inventory_test")
        .WithUsername("test_user")
        .WithPassword("test_pass")
        .Build();

    private InventoryDbContext _context = null!;
    private InventoryRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString())
            .Options;

        _context = new InventoryDbContext(options);
        await _context.Database.EnsureCreatedAsync();

        _repository = new InventoryRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _dbContainer.DisposeAsync();
    }

    private InventoryDbContext CreateFreshDbContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString())
            .Options;

        return new InventoryDbContext(options);
    }

    [Fact]
    public async Task AddAsync_ShouldPersistInventoryItem()
    {
        // Arrange
        var productId = ProductId.New();
        var item = InventoryItem.Create(productId, "SKU-PUMP-01", initialQuantity: 100);

        // Act
        await _repository.AddAsync(item);
        await _context.SaveChangesAsync();

        // Assert
        using var verificationContext = CreateFreshDbContext();
        var persisted = await verificationContext.InventoryItems
            .FirstOrDefaultAsync(x => x.ProductId == productId);

        persisted.Should().NotBeNull();
        persisted!.Sku.Should().Be("SKU-PUMP-01");
        persisted.QuantityOnHand.Should().Be(100);
        persisted.AllocatedQuantity.Should().Be(0);
        persisted.AvailableQuantity.Should().Be(100);
        persisted.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PessimisticLocking_UnderHighConcurrencyOnHotSku_ShouldExecuteAllReservationsSequentiallyWithoutExceptions()
    {
        // Arrange: Seed a hot product SKU with 100 units in stock
        var productId = ProductId.New();
        var hotItem = InventoryItem.Create(productId, "SKU-HOT-VALVE", initialQuantity: 100);

        await _repository.AddAsync(hotItem);
        await _context.SaveChangesAsync();

        const int concurrentTransactionsCount = 10;
        const int quantityPerReservation = 5;

        // Act: Launch 10 simultaneous concurrent transactions competing for the exact same row.
        // Under Optimistic Concurrency Control (OCC), 9 out of 10 would fail with DbUpdateConcurrencyException.
        // Under Pessimistic Locking (SELECT FOR UPDATE), all 10 acquire the row lock sequentially and succeed.
        var concurrentTasks = Enumerable.Range(0, concurrentTransactionsCount).Select(async _ =>
        {
            await using var taskContext = CreateFreshDbContext();
            var taskRepo = new InventoryRepository(taskContext);

            await using var transaction = await taskContext.Database.BeginTransactionAsync();

            var lockedItems = await taskRepo.GetByProductIdsForUpdateAsync([productId]);
            var itemToAllocate = lockedItems.Single();

            itemToAllocate.Allocate(quantityPerReservation);
            taskRepo.Update(itemToAllocate);

            await taskContext.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        // Await all concurrent allocations
        await Task.WhenAll(concurrentTasks);

        // Assert: All 10 transactions succeeded without a single concurrency exception.
        // Total allocated must equal 10 * 5 = 50, leaving 50 available.
        await using var verificationContext = CreateFreshDbContext();
        var updatedItem = await verificationContext.InventoryItems
            .AsNoTracking()
            .FirstAsync(x => x.ProductId == productId);

        updatedItem.AllocatedQuantity.Should().Be(concurrentTransactionsCount * quantityPerReservation);
        updatedItem.AvailableQuantity.Should().Be(100 - (concurrentTransactionsCount * quantityPerReservation));
        updatedItem.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task PessimisticLocking_WithOpposingMultiItemOrder_ShouldPreventDeadlocksViaDeterministicSorting()
    {
        // Arrange: Seed two distinct products
        var productA = ProductId.New();
        var productB = ProductId.New();

        var itemA = InventoryItem.Create(productA, "SKU-A", initialQuantity: 50);
        var itemB = InventoryItem.Create(productB, "SKU-B", initialQuantity: 50);

        await _repository.AddAsync(itemA);
        await _repository.AddAsync(itemB);
        await _context.SaveChangesAsync();

        // Act: Task 1 requests [B, A], Task 2 requests [A, B] concurrently.
        // Because GetByProductIdsForUpdateAsync sorts ProductIds deterministically before locking,
        // both transactions lock rows in the exact same global order (A then B), making deadlocks impossible.
        var task1 = Task.Run(async () =>
        {
            await using var context1 = CreateFreshDbContext();
            var repo1 = new InventoryRepository(context1);
            await using var tx1 = await context1.Database.BeginTransactionAsync();

            var items = await repo1.GetByProductIdsForUpdateAsync([productB, productA]);
            foreach (var item in items)
            {
                item.Allocate(2);
                repo1.Update(item);
            }

            await context1.SaveChangesAsync();
            await tx1.CommitAsync();
        });

        var task2 = Task.Run(async () =>
        {
            await using var context2 = CreateFreshDbContext();
            var repo2 = new InventoryRepository(context2);
            await using var tx2 = await context2.Database.BeginTransactionAsync();

            var items = await repo2.GetByProductIdsForUpdateAsync([productA, productB]);
            foreach (var item in items)
            {
                item.Allocate(3);
                repo2.Update(item);
            }

            await context2.SaveChangesAsync();
            await tx2.CommitAsync();
        });

        // Await both tasks: neither should fail or throw a 40P01 deadlock exception
        await Task.WhenAll(task1, task2);

        // Assert: Both allocations succeeded cleanly
        await using var verificationContext = CreateFreshDbContext();
        var finalItemA = await verificationContext.InventoryItems.FirstAsync(x => x.ProductId == productA);
        var finalItemB = await verificationContext.InventoryItems.FirstAsync(x => x.ProductId == productB);

        finalItemA.AllocatedQuantity.Should().Be(5); // 2 + 3
        finalItemB.AllocatedQuantity.Should().Be(5); // 2 + 3
    }
}
