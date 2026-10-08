using FluentAssertions;
using TradeFlow.Domain.Inventory;
using TradeFlow.Domain.Inventory.Events;
using TradeFlow.Domain.Inventory.Exceptions;
using TradeFlow.Domain.Common;
using Xunit;

namespace TradeFlow.Domain.UnitTests.Inventory;

public class InventoryItemTests
{
    private readonly ProductId _productId = ProductId.New();
    private const string Sku = "SKU-VALVE-100";

    [Fact]
    public void Create_WithValidParameters_ShouldInitializeCorrectly()
    {
        // Act
        var item = InventoryItem.Create(_productId, Sku, initialQuantity: 50);

        // Assert
        item.Id.Should().NotBeNull();
        item.ProductId.Should().Be(_productId);
        item.Sku.Should().Be("SKU-VALVE-100");
        item.QuantityOnHand.Should().Be(50);
        item.AllocatedQuantity.Should().Be(0);
        item.AvailableQuantity.Should().Be(50);
    }

    [Fact]
    public void Create_WithWhitespaceSku_ShouldThrowArgumentException()
    {
        // Act
        var act = () => InventoryItem.Create(_productId, "   ", 10);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("sku");
    }

    [Fact]
    public void Create_WithNegativeInitialQuantity_ShouldThrowInventoryInvariantException()
    {
        // Act
        var act = () => InventoryItem.Create(_productId, Sku, initialQuantity: -5);

        // Assert
        var ex = act.Should().Throw<InventoryInvariantException>().Which;
        ex.Code.Should().Be("INVALID_INVENTORY_QUANTITY");
    }

    [Fact]
    public void Allocate_WhenStockAvailable_ShouldIncreaseAllocatedQuantityAndEmitDomainEvent()
    {
        // Arrange
        var item = InventoryItem.Create(_productId, Sku, initialQuantity: 20);

        // Act
        item.Allocate(5);

        // Assert
        item.QuantityOnHand.Should().Be(20);
        item.AllocatedQuantity.Should().Be(5);
        item.AvailableQuantity.Should().Be(15);

        item.DomainEvents.Should().ContainSingle(e => e is StockAllocatedDomainEvent);
        var domainEvent = (StockAllocatedDomainEvent)item.DomainEvents.Single();
        domainEvent.ProductId.Should().Be(_productId);
        domainEvent.Sku.Should().Be(Sku);
        domainEvent.Quantity.Should().Be(5);
    }

    [Fact]
    public void Allocate_WhenRequestedExceedsAvailableStock_ShouldThrowInsufficientStockException()
    {
        // Arrange: 10 on hand, 8 already allocated -> 2 available
        var item = InventoryItem.Create(_productId, Sku, initialQuantity: 10);
        item.Allocate(8);

        // Act: Attempt to allocate 3 units when only 2 are available
        var act = () => item.Allocate(3);

        // Assert
        var ex = act.Should().Throw<InventoryInvariantException>().Which;
        ex.Code.Should().Be("INSUFFICIENT_STOCK");
        item.AllocatedQuantity.Should().Be(8); // State remains intact
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public void Allocate_WithZeroOrNegativeQuantity_ShouldThrowInventoryInvariantException(int invalidQuantity)
    {
        // Arrange
        var item = InventoryItem.Create(_productId, Sku, initialQuantity: 10);

        // Act
        var act = () => item.Allocate(invalidQuantity);

        // Assert
        var ex = act.Should().Throw<InventoryInvariantException>().Which;
        ex.Code.Should().Be("INVALID_INVENTORY_QUANTITY");
    }

    [Fact]
    public void Release_WhenAllocatedQuantitySufficient_ShouldDecreaseAllocatedQuantityAndEmitDomainEvent()
    {
        // Arrange: Allocate 10 units first
        var item = InventoryItem.Create(_productId, Sku, initialQuantity: 20);
        item.Allocate(10);
        item.ClearDomainEvents();

        // Act: Release 4 units back to available pool
        item.Release(4);

        // Assert
        item.AllocatedQuantity.Should().Be(6);
        item.AvailableQuantity.Should().Be(14);

        item.DomainEvents.Should().ContainSingle(e => e is StockReleasedDomainEvent);
        var domainEvent = (StockReleasedDomainEvent)item.DomainEvents.Single();
        domainEvent.Quantity.Should().Be(4);
    }

    [Fact]
    public void Release_WhenRequestedExceedsAllocatedQuantity_ShouldThrowExcessiveReleaseException()
    {
        // Arrange: Only 5 units allocated
        var item = InventoryItem.Create(_productId, Sku, initialQuantity: 20);
        item.Allocate(5);

        // Act: Attempt to release 6 units
        var act = () => item.Release(6);

        // Assert
        var ex = act.Should().Throw<InventoryInvariantException>().Which;
        ex.Code.Should().Be("EXCESSIVE_STOCK_RELEASE");
    }

    [Fact]
    public void Replenish_WithValidQuantity_ShouldIncreaseQuantityOnHandAndEmitDomainEvent()
    {
        // Arrange
        var item = InventoryItem.Create(_productId, Sku, initialQuantity: 10);

        // Act: Add 25 new units to warehouse
        item.Replenish(25);

        // Assert
        item.QuantityOnHand.Should().Be(35);
        item.AvailableQuantity.Should().Be(35);

        item.DomainEvents.Should().Contain(e => e is StockReplenishedDomainEvent);
        var domainEvent = item.DomainEvents.OfType<StockReplenishedDomainEvent>().Single();
        domainEvent.Quantity.Should().Be(25);
    }

    [Fact]
    public void CreatedAt_ShouldBeInitializedOnCreation_AndUpdatedAtNullInitially()
    {
        // Act
        var before = DateTimeOffset.UtcNow;
        var item = InventoryItem.Create(_productId, Sku, initialQuantity: 10);
        var after = DateTimeOffset.UtcNow;

        // Assert
        item.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        item.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Allocate_Release_Replenish_ShouldUpdateUpdatedAtTimestamp()
    {
        // Arrange
        var item = InventoryItem.Create(_productId, Sku, initialQuantity: 50);
        item.UpdatedAt.Should().BeNull();

        // Act 1: Allocate
        item.Allocate(10);
        item.UpdatedAt.Should().NotBeNull();
        var afterAllocate = item.UpdatedAt;

        // Act 2: Release
        item.Release(5);
        item.UpdatedAt.Should().BeOnOrAfter(afterAllocate!.Value);
        var afterRelease = item.UpdatedAt;

        // Act 3: Replenish
        item.Replenish(20);
        item.UpdatedAt.Should().BeOnOrAfter(afterRelease!.Value);
    }
}
