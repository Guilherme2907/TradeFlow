using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Inventory.Contracts;
using TradeFlow.Domain.Inventory;
using TradeFlow.Domain.Inventory.Exceptions;
using TradeFlow.Domain.Common;

namespace TradeFlow.Application.Inventory.Services;

/// <summary>
/// In-process implementation of the public IInventoryModule facade.
/// Coordinates inventory aggregate allocation within the monolith transaction boundary (ADR-0007).
/// </summary>
public class InventoryModule : IInventoryModule
{
    private readonly IInventoryRepository _inventoryRepository;

    public InventoryModule(IInventoryRepository inventoryRepository)
    {
        _inventoryRepository = inventoryRepository;
    }

    public async Task<Result> ReserveStockAsync(
        ReserveStockRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Items.Count == 0)
            return Result.Success();

        // 1. Aggregate requested quantities per product and sort deterministically to prevent AB-BA deadlocks
        var groupedRequests = request.Items
            .GroupBy(i => i.ProductId)
            .Select(g => (
                ProductId: new ProductId(g.Key),
                Sku: g.First().Sku,
                TotalQuantity: g.Sum(x => x.Quantity)
            ))
            .OrderBy(x => x.ProductId.Value)
            .ToList();

        var productIds = groupedRequests.Select(x => x.ProductId).ToList();

        // 2. Fetch all required inventory aggregates with pessimistic row locks (SELECT FOR UPDATE)
        var lockedItems = await _inventoryRepository.GetByProductIdsForUpdateAsync(productIds, cancellationToken);
        var itemMap = lockedItems.ToDictionary(x => x.ProductId);

        var itemsToAllocate = new List<(InventoryItem Item, int Quantity)>();

        // 3. Phase 1: Validate existence and stock availability across all requested items
        foreach (var req in groupedRequests)
        {
            if (!itemMap.TryGetValue(req.ProductId, out var inventoryItem))
            {
                return Result.Failure(Error.NotFound(
                    "INVENTORY_ITEM_NOT_FOUND",
                    $"No inventory tracking found for product '{req.ProductId.Value}' (SKU '{req.Sku}')."));
            }

            if (inventoryItem.AvailableQuantity < req.TotalQuantity)
            {
                return Result.Failure(Error.Conflict(
                    "INSUFFICIENT_STOCK",
                    $"Insufficient available stock for SKU '{req.Sku}'. Requested: {req.TotalQuantity}, Available: {inventoryItem.AvailableQuantity}."));
            }

            itemsToAllocate.Add((inventoryItem, req.TotalQuantity));
        }

        // 4. Phase 2: Execute domain allocation on each inventory aggregate
        foreach (var (item, quantity) in itemsToAllocate)
        {
            try
            {
                item.Allocate(quantity);
                _inventoryRepository.Update(item);
            }
            catch (InventoryInvariantException ex)
            {
                return Result.Failure(Error.Conflict(ex.Code, ex.Message));
            }
        }

        return Result.Success();
    }

    public Task<Result> ReleaseStockAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        // Reserved for order cancellation / timeout flows in Phase 2
        return Task.FromResult(Result.Success());
    }
}
