using FluentValidation;
using MediatR;
using TradeFlow.Application.Common.Caching;
using TradeFlow.Application.Common.Models;

namespace TradeFlow.Application.Catalog.Queries.GetProductById;

public sealed record GetProductByIdQuery(Guid ProductId)
    : IRequest<Result<ProductResponse>>, ICacheableQuery
{
    public string CacheKey => $"catalog:product:{ProductId}";
    public TimeSpan? Expiration => TimeSpan.FromMinutes(5);
}

public sealed class GetProductByIdQueryValidator : AbstractValidator<GetProductByIdQuery>
{
    public GetProductByIdQueryValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty()
            .WithMessage("ProductId is required.");
    }
}
