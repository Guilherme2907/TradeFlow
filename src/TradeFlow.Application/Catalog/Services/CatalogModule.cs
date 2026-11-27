using TradeFlow.Application.Catalog.Contracts;
using TradeFlow.Application.Common.Models;
using TradeFlow.Domain.Catalog;
using TradeFlow.Domain.Common;

namespace TradeFlow.Application.Catalog.Services;

public sealed class CatalogModule : ICatalogModule
{
    private readonly IProductRepository _productRepository;

    public CatalogModule(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Result<CatalogProductSnapshot>> GetProductSnapshotAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(productId), cancellationToken);
        if (product is null)
        {
            return Result.Failure<CatalogProductSnapshot>(
                Error.NotFound("CATALOG_PRODUCT_NOT_FOUND", $"Product '{productId}' was not found in the catalog."));
        }

        return Result.Success(new CatalogProductSnapshot(
            product.Id.Value,
            product.Sku,
            product.Name,
            product.Category,
            product.UnitPrice,
            product.Currency,
            product.UnitOfMeasure,
            product.MinimumOrderQuantity,
            product.IsActive,
            product.TechnicalDescription));
    }

    public async Task<IReadOnlyList<CatalogProductSnapshot>> ListActiveProductsAsync(
        CancellationToken cancellationToken = default)
    {
        var (items, _) = await _productRepository.SearchAsync(
            search: null,
            category: null,
            currency: null,
            activeOnly: true,
            page: 1,
            pageSize: 100,
            cancellationToken: cancellationToken);

        return items
            .Select(product => new CatalogProductSnapshot(
                product.Id.Value,
                product.Sku,
                product.Name,
                product.Category,
                product.UnitPrice,
                product.Currency,
                product.UnitOfMeasure,
                product.MinimumOrderQuantity,
                product.IsActive,
                product.TechnicalDescription))
            .ToList();
    }
}
