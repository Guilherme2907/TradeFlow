using MediatR;
using TradeFlow.Application.Common.Caching;
using TradeFlow.Application.Common.Models;
using TradeFlow.Domain.Catalog;
using TradeFlow.Domain.Catalog.Exceptions;
using TradeFlow.Domain.Common;

namespace TradeFlow.Application.Catalog.Commands.UpdateProduct;

public sealed class UpdateProductCommandHandler
    : IRequestHandler<UpdateProductCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly ICacheService _cacheService;

    public UpdateProductCommandHandler(
        IProductRepository productRepository,
        ICacheService cacheService)
    {
        _productRepository = productRepository;
        _cacheService = cacheService;
    }

    public async Task<Result> Handle(
        UpdateProductCommand request,
        CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(request.ProductId), cancellationToken);
        if (product is null)
        {
            return Result.Failure(
                Error.NotFound("CATALOG_PRODUCT_NOT_FOUND", $"Product '{request.ProductId}' was not found."));
        }

        try
        {
            product.UpdateDetails(
                request.Name,
                request.Category,
                request.TechnicalDescription,
                request.UnitOfMeasure,
                request.MinimumOrderQuantity);

            product.UpdatePrice(request.UnitPrice, request.Currency);

            if (request.IsActive)
                product.Activate();
            else
                product.Deactivate();
        }
        catch (CatalogInvariantException ex)
        {
            return Result.Failure(Error.Validation(ex.Code, ex.Message));
        }

        _productRepository.Update(product);
        await _productRepository.SaveChangesAsync(cancellationToken);

        await _cacheService.RemoveAsync($"catalog:product:{request.ProductId}", cancellationToken);

        return Result.Success();
    }
}
