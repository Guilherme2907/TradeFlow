using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Inventory.Contracts;
using TradeFlow.Application.Orders.Gateways;
using TradeFlow.Domain.Orders;

namespace TradeFlow.Infrastructure.Modules.Orders.Gateways;

/// <summary>
/// Adapter implementing the Orders module's IInventoryReservationService gateway
/// by delegating to the public IInventoryModule interface (ADR-0007).
/// </summary>
public class InventoryReservationServiceAdapter : IInventoryReservationService
{
    private readonly IInventoryModule _inventoryModule;

    public InventoryReservationServiceAdapter(IInventoryModule inventoryModule)
    {
        _inventoryModule = inventoryModule;
    }

    public Task<Result> ReserveStockAsync(
        OrderId orderId,
        IReadOnlyCollection<ReservationItem> items,
        CancellationToken cancellationToken = default)
    {
        var request = new ReserveStockRequest(
            orderId.Value,
            items.Select(i => new ReservationItemDto(i.ProductId.Value, i.Sku, i.Quantity)).ToList());

        return _inventoryModule.ReserveStockAsync(request, cancellationToken);
    }
}
