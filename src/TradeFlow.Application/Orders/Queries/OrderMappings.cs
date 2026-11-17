using TradeFlow.Application.Orders.Queries.GetOrderById;
using TradeFlow.Domain.Orders;

namespace TradeFlow.Application.Orders.Queries;

internal static class OrderMappings
{
    public static OrderResponse ToResponse(this Order order)
    {
        var lines = order.Lines.Select(l => new OrderLineResponse(
            LineId: l.Id.Value,
            ProductId: l.ProductId.Value,
            Sku: l.Sku,
            ProductName: l.ProductName,
            UnitPrice: l.UnitPrice.Amount,
            Currency: l.UnitPrice.Currency,
            Quantity: l.Quantity,
            Total: l.LineTotal.Amount)).ToList();

        return new OrderResponse(
            OrderId: order.Id.Value,
            CustomerId: order.CustomerId.Value,
            Status: order.Status.ToString(),
            Currency: order.Currency,
            TotalAmount: order.TotalAmount.Amount,
            Lines: lines,
            CreatedAt: order.CreatedAt,
            SubmittedAt: order.SubmittedAt,
            CancelledAt: order.CancelledAt,
            CancellationReason: order.CancellationReason);
    }
}
