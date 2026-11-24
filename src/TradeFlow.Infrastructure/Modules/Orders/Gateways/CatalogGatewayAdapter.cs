using TradeFlow.Application.Catalog.Contracts;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Orders.Gateways;

namespace TradeFlow.Infrastructure.Modules.Orders.Gateways;

/// <summary>
/// Adapter implementing the Orders module's ICatalogGateway port by delegating
/// to the public ICatalogModule contract (ADR-0007).
/// </summary>
public sealed class CatalogGatewayAdapter : ICatalogGateway
{
    private readonly ICatalogModule _catalogModule;

    public CatalogGatewayAdapter(ICatalogModule catalogModule)
    {
        _catalogModule = catalogModule;
    }

    public async Task<Result<OrderCatalogProductSnapshot>> GetProductSnapshotAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var result = await _catalogModule.GetProductSnapshotAsync(productId, cancellationToken);
        if (result.IsFailure)
        {
            return Result.Failure<OrderCatalogProductSnapshot>(result.Error);
        }

        var p = result.Value;
        return Result.Success(new OrderCatalogProductSnapshot(
            p.ProductId,
            p.Sku,
            p.Name,
            p.UnitPrice,
            p.Currency,
            p.MinimumOrderQuantity,
            p.IsActive));
    }
}
