namespace TradeFlow.Application.Inventory.Contracts;

public sealed record ReserveStockRequest(
    Guid OrderId,
    IReadOnlyCollection<ReservationItemDto> Items);

public sealed record ReservationItemDto(
    Guid ProductId,
    string Sku,
    int Quantity);
