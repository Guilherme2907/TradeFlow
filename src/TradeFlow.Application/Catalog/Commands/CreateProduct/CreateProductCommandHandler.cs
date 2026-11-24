using MediatR;
using TradeFlow.Application.Common.Models;
using TradeFlow.Domain.Catalog;
using TradeFlow.Domain.Catalog.Exceptions;

namespace TradeFlow.Application.Catalog.Commands.CreateProduct;

public sealed class CreateProductCommandHandler
    : IRequestHandler<CreateProductCommand, Result<Guid>>
{
    private readonly IProductRepository _productRepository;

    public CreateProductCommandHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Result<Guid>> Handle(
        CreateProductCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedSku = request.Sku.Trim().ToUpperInvariant();
        var existing = await _productRepository.GetBySkuAsync(normalizedSku, cancellationToken);
        if (existing is not null)
        {
            return Result.Failure<Guid>(
                Error.Conflict("CATALOG_DUPLICATE_SKU", $"A product with SKU '{normalizedSku}' already exists in the catalog."));
        }

        Product product;
        try
        {
            product = Product.Create(
                request.Sku,
                request.Name,
                request.Category,
                request.TechnicalDescription,
                request.UnitPrice,
                request.Currency,
                request.UnitOfMeasure,
                request.MinimumOrderQuantity);
        }
        catch (CatalogInvariantException ex)
        {
            return Result.Failure<Guid>(Error.Validation(ex.Code, ex.Message));
        }

        await _productRepository.AddAsync(product, cancellationToken);
        await _productRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(product.Id.Value);
    }
}
