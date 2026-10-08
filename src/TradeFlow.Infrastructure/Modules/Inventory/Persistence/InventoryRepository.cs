using Microsoft.EntityFrameworkCore;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Inventory;

namespace TradeFlow.Infrastructure.Modules.Inventory.Persistence;

public class InventoryRepository : IInventoryRepository
{
    private readonly InventoryDbContext _context;

    public InventoryRepository(InventoryDbContext context)
    {
        _context = context;
    }

    public async Task<InventoryItem?> GetByProductIdAsync(ProductId productId, CancellationToken cancellationToken = default)
    {
        return await _context.InventoryItems
            .FirstOrDefaultAsync(x => x.ProductId == productId, cancellationToken);
    }

    public async Task<InventoryItem?> GetBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        var normalizedSku = sku.Trim().ToUpperInvariant();
        return await _context.InventoryItems
            .FirstOrDefaultAsync(x => x.Sku == normalizedSku, cancellationToken);
    }

    public async Task AddAsync(InventoryItem item, CancellationToken cancellationToken = default)
    {
        await _context.InventoryItems.AddAsync(item, cancellationToken);
    }

    public void Update(InventoryItem item)
    {
        _context.InventoryItems.Update(item);
    }
}
