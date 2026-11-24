using MediatR;
using TradeFlow.Application.Common.Models;
using TradeFlow.Domain.Catalog;

namespace TradeFlow.Application.Catalog.Queries.GetProducts;

public sealed class GetProductsQueryHandler
    : IRequestHandler<GetProductsQuery, Result<PagedResponse<ProductResponse>>>
{
    private readonly IProductRepository _productRepository;

    public GetProductsQueryHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Result<PagedResponse<ProductResponse>>> Handle(
        GetProductsQuery request,
        CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _productRepository.SearchAsync(
            request.Search,
            request.Category,
            request.Currency,
            request.ActiveOnly,
            request.Page,
            request.PageSize,
            cancellationToken);

        var response = new PagedResponse<ProductResponse>(
            items.Select(p => p.ToResponse()).ToList(),
            request.Page,
            request.PageSize,
            totalCount);

        return Result.Success(response);
    }
}
