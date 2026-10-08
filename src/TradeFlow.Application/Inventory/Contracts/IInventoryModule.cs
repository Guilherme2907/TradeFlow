using TradeFlow.Application.Common.Models;

namespace TradeFlow.Application.Inventory.Contracts;

/// <summary>
/// Public in-process contract interface exposed by the Inventory bounded context.
/// Consumed by the Orders context to perform stock reservation and release
/// without coupling to internal Inventory domain entities or repositories.
/// </summary>
public interface IInventoryModule
{
    Task<Result> ReserveStockAsync(
        ReserveStockRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> ReleaseStockAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);
}
