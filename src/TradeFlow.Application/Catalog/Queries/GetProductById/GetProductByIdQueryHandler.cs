using MediatR;
using TradeFlow.Application.Common.Models;
using TradeFlow.Domain.Catalog;
using TradeFlow.Domain.Common;

namespace TradeFlow.Application.Catalog.Queries.GetProductById;

public sealed class GetProductByIdQueryHandler
    : IRequestHandler<GetProductByIdQuery, Result<ProductResponse>>
{
    private readonly IProductRepository _productRepository;

    public GetProductByIdQueryHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Result<ProductResponse>> Handle(
        GetProductByIdQuery request,
        CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(request.ProductId), cancellationToken);
        if (product is null)
        {
            return Result.Failure<ProductResponse>(
                Error.NotFound("CATALOG_PRODUCT_NOT_FOUND", $"Product '{request.ProductId}' was not found."));
        }

        return Result.Success(product.ToResponse());
    }
}
