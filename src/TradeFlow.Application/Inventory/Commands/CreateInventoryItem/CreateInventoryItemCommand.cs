using FluentValidation;
using MediatR;
using TradeFlow.Application.Common.Models;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Inventory;
using TradeFlow.Domain.Inventory.Exceptions;

namespace TradeFlow.Application.Inventory.Commands.CreateInventoryItem;

public sealed record CreateInventoryItemCommand(
    Guid ProductId,
    string Sku,
    int InitialQuantity) : IRequest<Result<Guid>>;

public sealed class CreateInventoryItemCommandValidator : AbstractValidator<CreateInventoryItemCommand>
{
    public CreateInventoryItemCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(100);
        RuleFor(x => x.InitialQuantity).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateInventoryItemCommandHandler
    : IRequestHandler<CreateInventoryItemCommand, Result<Guid>>
{
    private readonly IInventoryRepository _repository;

    public CreateInventoryItemCommandHandler(IInventoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<Guid>> Handle(
        CreateInventoryItemCommand request,
        CancellationToken cancellationToken)
    {
        var productId = new ProductId(request.ProductId);
        var existingByProduct = await _repository.GetByProductIdAsync(productId, cancellationToken);
        if (existingByProduct is not null)
        {
            return Result.Failure<Guid>(
                Error.Conflict("INVENTORY_PRODUCT_EXISTS", $"Inventory tracking already exists for product '{request.ProductId}'."));
        }

        var normalizedSku = request.Sku.Trim().ToUpperInvariant();
        var existingBySku = await _repository.GetBySkuAsync(normalizedSku, cancellationToken);
        if (existingBySku is not null)
        {
            return Result.Failure<Guid>(
                Error.Conflict("INVENTORY_DUPLICATE_SKU", $"Inventory tracking already exists for SKU '{normalizedSku}'."));
        }

        InventoryItem item;
        try
        {
            item = InventoryItem.Create(productId, request.Sku, request.InitialQuantity);
        }
        catch (InventoryInvariantException ex)
        {
            return Result.Failure<Guid>(Error.Validation(ex.Code, ex.Message));
        }

        await _repository.AddAsync(item, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result.Success(item.Id.Value);
    }
}
