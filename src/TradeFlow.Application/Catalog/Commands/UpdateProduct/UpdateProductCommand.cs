using FluentValidation;
using MediatR;
using TradeFlow.Application.Common.Models;

namespace TradeFlow.Application.Catalog.Commands.UpdateProduct;

public sealed record UpdateProductCommand(
    Guid ProductId,
    string Name,
    string Category,
    string TechnicalDescription,
    decimal UnitPrice,
    string Currency,
    string UnitOfMeasure,
    int MinimumOrderQuantity,
    bool IsActive) : IRequest<Result>;

public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Category).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TechnicalDescription).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.UnitPrice).GreaterThan(0m);
        RuleFor(x => x.Currency)
            .NotEmpty()
            .Length(3)
            .Matches("^[A-Za-z]{3}$")
            .WithMessage("Currency must be a 3-letter ISO code.");
        RuleFor(x => x.UnitOfMeasure).NotEmpty().MaximumLength(50);
        RuleFor(x => x.MinimumOrderQuantity).GreaterThanOrEqualTo(1);
    }
}
