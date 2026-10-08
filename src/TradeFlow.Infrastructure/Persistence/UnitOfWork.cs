using TradeFlow.Application.Orders.Gateways;
using TradeFlow.Infrastructure.Modules.Inventory.Persistence;
using TradeFlow.Infrastructure.Modules.Orders.Persistence;

namespace TradeFlow.Infrastructure.Persistence;

/// <summary>
/// Unit of work coordinating persistence across bounded contexts
/// within the single-database modular monolith transaction boundary (ADR-0002 & ADR-0007).
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly OrdersDbContext _ordersDbContext;
    private readonly InventoryDbContext _inventoryDbContext;

    public UnitOfWork(OrdersDbContext ordersDbContext, InventoryDbContext inventoryDbContext)
    {
        _ordersDbContext = ordersDbContext;
        _inventoryDbContext = inventoryDbContext;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var ordersSaved = await _ordersDbContext.SaveChangesAsync(cancellationToken);
        var inventorySaved = await _inventoryDbContext.SaveChangesAsync(cancellationToken);
        return ordersSaved + inventorySaved;
    }
}
