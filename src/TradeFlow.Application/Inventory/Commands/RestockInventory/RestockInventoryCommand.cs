using FluentValidation;
using MediatR;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Inventory.Queries.GetInventoryItems;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Inventory;
using TradeFlow.Domain.Inventory.Exceptions;

namespace TradeFlow.Application.Inventory.Commands.RestockInventory;

public sealed record RestockInventoryCommand(
    Guid ProductId,
    int Quantity) : IRequest<Result<InventoryItemResponse>>;

public sealed class RestockInventoryCommandValidator : AbstractValidator<RestockInventoryCommand>
{
    public RestockInventoryCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class RestockInventoryCommandHandler
    : IRequestHandler<RestockInventoryCommand, Result<InventoryItemResponse>>
{
    private readonly IInventoryRepository _repository;

    public RestockInventoryCommandHandler(IInventoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<InventoryItemResponse>> Handle(
        RestockInventoryCommand request,
        CancellationToken cancellationToken)
    {
        var item = await _repository.GetByProductIdAsync(new ProductId(request.ProductId), cancellationToken);
        if (item is null)
        {
            return Result.Failure<InventoryItemResponse>(
                Error.NotFound("INVENTORY_ITEM_NOT_FOUND", $"No inventory record found for product '{request.ProductId}'."));
        }

        try
        {
            item.Replenish(request.Quantity);
        }
        catch (InventoryInvariantException ex)
        {
            return Result.Failure<InventoryItemResponse>(Error.Validation(ex.Code, ex.Message));
        }

        _repository.Update(item);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result.Success(new InventoryItemResponse(
            item.ProductId.Value,
            item.Sku,
            item.QuantityOnHand,
            item.AllocatedQuantity,
            item.AvailableQuantity));
    }
}
