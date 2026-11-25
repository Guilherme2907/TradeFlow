using TradeFlow.Application.Common.Models;

namespace TradeFlow.Application.Catalog.Contracts;

public sealed record CatalogProductSnapshot(
    Guid ProductId,
    string Sku,
    string Name,
    string Category,
    decimal UnitPrice,
    string Currency,
    string UnitOfMeasure,
    int MinimumOrderQuantity,
    bool IsActive);

/// <summary>
/// Explicit in-process public contract exposed by the Catalog bounded context (ADR-0007).
/// External modules (such as Orders and upcoming TradeFlow.AI) interact with Catalog solely through this contract.
/// </summary>
public interface ICatalogModule
{
    Task<Result<CatalogProductSnapshot>> GetProductSnapshotAsync(
        Guid productId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogProductSnapshot>> ListActiveProductsAsync(
        CancellationToken cancellationToken = default);
}
