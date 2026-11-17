using MediatR;
using TradeFlow.Application.Common.Models;
using TradeFlow.Domain.Inventory;

namespace TradeFlow.Application.Inventory.Queries.GetInventoryItems;

public sealed class GetInventoryItemsQueryHandler : IRequestHandler<GetInventoryItemsQuery, Result<IReadOnlyList<InventoryItemResponse>>>
{
    private readonly IInventoryRepository _inventoryRepository;

    public GetInventoryItemsQueryHandler(IInventoryRepository inventoryRepository)
    {
        _inventoryRepository = inventoryRepository;
    }

    public async Task<Result<IReadOnlyList<InventoryItemResponse>>> Handle(
        GetInventoryItemsQuery request,
        CancellationToken cancellationToken)
    {
        var items = await _inventoryRepository.GetAllAsync(cancellationToken);

        IReadOnlyList<InventoryItemResponse> response = items
            .Select(item => new InventoryItemResponse(
                ProductId: item.ProductId.Value,
                Sku: item.Sku,
                QuantityOnHand: item.QuantityOnHand,
                AllocatedQuantity: item.AllocatedQuantity,
                AvailableQuantity: item.AvailableQuantity))
            .ToList();

        return Result.Success(response);
    }
}
