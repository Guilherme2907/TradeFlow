using MediatR;
using TradeFlow.Application.Common.Models;

namespace TradeFlow.Application.Inventory.Queries.GetInventoryItems;

public sealed record InventoryItemResponse(
    Guid ProductId,
    string Sku,
    int QuantityOnHand,
    int AllocatedQuantity,
    int AvailableQuantity);

public sealed record GetInventoryItemsQuery : IRequest<Result<IReadOnlyList<InventoryItemResponse>>>;
