using TradeFlow.Domain.Common;
using TradeFlow.Domain.Orders.Exceptions;

namespace TradeFlow.Domain.Orders;

/// <summary>
/// Represents an individual item within an Order.
/// Encapsulates an immutable point-in-time commercial snapshot of the product per ADR-0001.
/// Managed exclusively through the Order Aggregate Root.
/// </summary>
public class OrderLine : Entity<OrderLineId>
{
    public ProductId ProductId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public Money UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public Money LineTotal => UnitPrice * Quantity;

    // Required by EF Core
    private OrderLine() { }

    internal OrderLine(
        OrderLineId id,
        ProductId productId,
        string sku,
        string productName,
        Money unitPrice,
        int quantity) : base(id)
    {
        if (string.IsNullOrWhiteSpace(sku))
            throw new ArgumentException("SKU snapshot cannot be empty.", nameof(sku));

        if (string.IsNullOrWhiteSpace(productName))
            throw new ArgumentException("Product name snapshot cannot be empty.", nameof(productName));

        if (quantity <= 0)
            throw OrderInvariantException.InvalidQuantity(quantity);

        ProductId = productId;
        Sku = sku.Trim().ToUpperInvariant();
        ProductName = productName.Trim();
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    internal void UpdateQuantity(int newQuantity)
    {
        if (newQuantity <= 0)
            throw OrderInvariantException.InvalidQuantity(newQuantity);

        Quantity = newQuantity;
    }
}
