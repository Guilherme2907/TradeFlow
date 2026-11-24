using TradeFlow.Application.Common.Models;

namespace TradeFlow.Application.Orders.Gateways;

public sealed record OrderCatalogProductSnapshot(
    Guid ProductId,
    string Sku,
    string Name,
    decimal UnitPrice,
    string Currency,
    int MinimumOrderQuantity,
    bool IsActive);

/// <summary>
/// Outbound Port owned by the Orders module to query authoritative product snapshots from Catalog (ADR-0007).
/// Swapping from in-process Modular Monolith to an HTTP/gRPC Catalog microservice requires only replacing
/// the Infrastructure adapter, with zero changes in Orders Application or Domain.
/// </summary>
public interface ICatalogGateway
{
    Task<Result<OrderCatalogProductSnapshot>> GetProductSnapshotAsync(
        Guid productId,
        CancellationToken cancellationToken = default);
}
