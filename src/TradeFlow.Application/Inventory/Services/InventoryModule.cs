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
        var itemsToAllocate = new List<(InventoryItem Item, int Quantity)>();

        // Phase 1: Validate stock availability across all requested items
        foreach (var itemDto in request.Items)
        {
            var inventoryItem = await _inventoryRepository.GetByProductIdAsync(
                new ProductId(itemDto.ProductId), cancellationToken);

            if (inventoryItem is null)
            {
                return Result.Failure(Error.NotFound(
                    "INVENTORY_ITEM_NOT_FOUND",
                    $"No inventory tracking found for product '{itemDto.ProductId}' (SKU '{itemDto.Sku}')."));
            }

            if (inventoryItem.AvailableQuantity < itemDto.Quantity)
            {
                return Result.Failure(Error.Conflict(
                    "INSUFFICIENT_STOCK",
                    $"Insufficient available stock for SKU '{itemDto.Sku}'. Requested: {itemDto.Quantity}, Available: {inventoryItem.AvailableQuantity}."));
            }

            itemsToAllocate.Add((inventoryItem, itemDto.Quantity));
        }

        // Phase 2: Execute domain allocation on each inventory aggregate
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
