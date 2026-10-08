using TradeFlow.Domain.Common;

namespace TradeFlow.Domain.Inventory.Exceptions;

public class InventoryInvariantException : DomainException
{
    public InventoryInvariantException(string code, string message) : base(code, message)
    {
    }

    public static InventoryInvariantException InsufficientStock(string sku, int requested, int available) =>
        new("INSUFFICIENT_STOCK",
            $"Insufficient available stock for SKU '{sku}'. Requested: {requested}, Available: {available}.");

    public static InventoryInvariantException InvalidQuantity(int quantity) =>
        new("INVALID_INVENTORY_QUANTITY",
            $"Inventory operation quantity must be strictly greater than zero. Received: {quantity}.");

    public static InventoryInvariantException ExcessiveRelease(string sku, int requestedRelease, int allocated) =>
        new("EXCESSIVE_STOCK_RELEASE",
            $"Cannot release {requestedRelease} units for SKU '{sku}' because only {allocated} units are currently allocated.");
}
