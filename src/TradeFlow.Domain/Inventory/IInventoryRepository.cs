using TradeFlow.Domain.Common;

namespace TradeFlow.Domain.Inventory;

public interface IInventoryRepository
{
    Task<InventoryItem?> GetByProductIdAsync(ProductId productId, CancellationToken cancellationToken = default);
    Task<InventoryItem?> GetByProductIdForUpdateAsync(ProductId productId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryItem>> GetByProductIdsForUpdateAsync(IReadOnlyCollection<ProductId> productIds, CancellationToken cancellationToken = default);
    Task<InventoryItem?> GetBySkuAsync(string sku, CancellationToken cancellationToken = default);
    Task AddAsync(InventoryItem item, CancellationToken cancellationToken = default);
    void Update(InventoryItem item);
}
