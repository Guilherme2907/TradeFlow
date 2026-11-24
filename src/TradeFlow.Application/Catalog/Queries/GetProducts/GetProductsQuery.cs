using FluentValidation;
using MediatR;
using TradeFlow.Application.Common.Models;

namespace TradeFlow.Application.Catalog.Queries.GetProducts;

public sealed record GetProductsQuery(
    string? Search = null,
    string? Category = null,
    string? Currency = null,
    bool? ActiveOnly = true,
    int Page = 1,
    int PageSize = 20) : IRequest<Result<PagedResponse<ProductResponse>>>;

public sealed class GetProductsQueryValidator : AbstractValidator<GetProductsQuery>
{
    public GetProductsQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be at least 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("PageSize must be between 1 and 100.");

        RuleFor(x => x.Currency)
            .Length(3)
            .When(x => !string.IsNullOrWhiteSpace(x.Currency))
            .WithMessage("Currency filter must be a 3-letter ISO code.");
    }
}
