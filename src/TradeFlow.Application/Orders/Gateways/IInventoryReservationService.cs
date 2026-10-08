using TradeFlow.Application.Common.Models;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Orders;

namespace TradeFlow.Application.Orders.Gateways;

public readonly record struct ReservationItem(ProductId ProductId, string Sku, int Quantity);

/// <summary>
/// Gateway interface abstracting inventory stock reservations per ADR-0002.
/// Keeps Orders and Inventory bounded contexts decoupled while executing within
/// the same monolith transaction boundary in Phase 1.
/// </summary>
public interface IInventoryReservationService
{
    Task<Result> ReserveStockAsync(
        OrderId orderId,
        IReadOnlyCollection<ReservationItem> items,
        CancellationToken cancellationToken = default);
}
