using TradeFlow.Domain.Common;
using TradeFlow.Domain.Inventory.Events;
using TradeFlow.Domain.Inventory.Exceptions;

namespace TradeFlow.Domain.Inventory;

/// <summary>
/// Aggregate root representing an inventory item for a specific product SKU.
/// Encapsulates all invariants governing physical stock (QuantityOnHand),
/// reserved stock (AllocatedQuantity), and available stock for sale.
/// </summary>
public class InventoryItem : AggregateRoot<InventoryItemId>
{
    public ProductId ProductId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public int QuantityOnHand { get; private set; }
    public int AllocatedQuantity { get; private set; }

    /// <summary>
    /// Computed available quantity that can be safely reserved or allocated.
    /// Invariant: AvailableQuantity = QuantityOnHand - AllocatedQuantity.
    /// </summary>
    public int AvailableQuantity => QuantityOnHand - AllocatedQuantity;

    // Required by EF Core for entity materialization
    private InventoryItem() { }

    private InventoryItem(
        InventoryItemId id,
        ProductId productId,
        string sku,
        int initialQuantity,
        DateTimeOffset? createdAt = null) : base(id, createdAt)
    {
        if (string.IsNullOrWhiteSpace(sku))
            throw new ArgumentException("SKU must not be empty.", nameof(sku));

        if (initialQuantity < 0)
            throw InventoryInvariantException.InvalidQuantity(initialQuantity);

        ProductId = productId;
        Sku = sku.Trim().ToUpperInvariant();
        QuantityOnHand = initialQuantity;
        AllocatedQuantity = 0;
    }

    public static InventoryItem Create(
        ProductId productId,
        string sku,
        int initialQuantity = 0,
        DateTimeOffset? createdAt = null)
    {
        return new InventoryItem(InventoryItemId.New(), productId, sku, initialQuantity, createdAt);
    }

    /// <summary>
    /// Allocates/reserves stock for a purchase order.
    /// Invariant: Requested quantity must not exceed current AvailableQuantity.
    /// </summary>
    public void Allocate(int quantity)
    {
        if (quantity <= 0)
            throw InventoryInvariantException.InvalidQuantity(quantity);

        if (quantity > AvailableQuantity)
            throw InventoryInvariantException.InsufficientStock(Sku, quantity, AvailableQuantity);

        AllocatedQuantity += quantity;
        Touch();

        RaiseDomainEvent(new StockAllocatedDomainEvent(Id, ProductId, Sku, quantity, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Releases previously allocated stock back to available inventory
    /// (e.g., when an order is cancelled or times out).
    /// </summary>
    public void Release(int quantity)
    {
        if (quantity <= 0)
            throw InventoryInvariantException.InvalidQuantity(quantity);

        if (quantity > AllocatedQuantity)
            throw InventoryInvariantException.ExcessiveRelease(Sku, quantity, AllocatedQuantity);

        AllocatedQuantity -= quantity;
        Touch();

        RaiseDomainEvent(new StockReleasedDomainEvent(Id, ProductId, Sku, quantity, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Replenishes physical stock arriving at the warehouse.
    /// </summary>
    public void Replenish(int quantity)
    {
        if (quantity <= 0)
            throw InventoryInvariantException.InvalidQuantity(quantity);

        QuantityOnHand += quantity;
        Touch();

        RaiseDomainEvent(new StockReplenishedDomainEvent(Id, ProductId, Sku, quantity, DateTimeOffset.UtcNow));
    }
}
