using FluentAssertions;
using NSubstitute;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Inventory.Contracts;
using TradeFlow.Domain.Inventory;
using TradeFlow.Domain.Common;
using TradeFlow.Application.Inventory.Services;
using Xunit;

namespace TradeFlow.Application.UnitTests.Inventory;

public class InventoryModuleTests
{
    private readonly IInventoryRepository _inventoryRepository = Substitute.For<IInventoryRepository>();
    private readonly InventoryModule _module;

    public InventoryModuleTests()
    {
        _module = new InventoryModule(_inventoryRepository);
    }

    [Fact]
    public async Task ReserveStockAsync_WhenAllItemsAvailable_ShouldAllocateAndReturnSuccess()
    {
        // Arrange
        var productId1 = ProductId.New();
        var item1 = InventoryItem.Create(productId1, "SKU-PUMP", initialQuantity: 100);

        var productId2 = ProductId.New();
        var item2 = InventoryItem.Create(productId2, "SKU-VALVE", initialQuantity: 50);

        _inventoryRepository.GetByProductIdsForUpdateAsync(
                Arg.Any<IReadOnlyCollection<ProductId>>(),
                Arg.Any<CancellationToken>())
            .Returns([item1, item2]);

        var request = new ReserveStockRequest(
            Guid.NewGuid(),
            [
                new ReservationItemDto(productId1.Value, "SKU-PUMP", 10),
                new ReservationItemDto(productId2.Value, "SKU-VALVE", 5)
            ]);

        // Act
        var result = await _module.ReserveStockAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        item1.AllocatedQuantity.Should().Be(10);
        item1.AvailableQuantity.Should().Be(90);
        item2.AllocatedQuantity.Should().Be(5);
        item2.AvailableQuantity.Should().Be(45);

        _inventoryRepository.Received(1).Update(item1);
        _inventoryRepository.Received(1).Update(item2);
    }

    [Fact]
    public async Task ReserveStockAsync_WhenProductNotFound_ShouldReturnNotFoundErrorAndNotAllocate()
    {
        // Arrange: Missing item not returned by repository
        var missingProductId = ProductId.New();
        _inventoryRepository.GetByProductIdsForUpdateAsync(
                Arg.Any<IReadOnlyCollection<ProductId>>(),
                Arg.Any<CancellationToken>())
            .Returns([]);

        var request = new ReserveStockRequest(
            Guid.NewGuid(),
            [
                new ReservationItemDto(missingProductId.Value, "SKU-UNKNOWN", 2)
            ]);

        // Act
        var result = await _module.ReserveStockAsync(request, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("INVENTORY_ITEM_NOT_FOUND");

        _inventoryRepository.DidNotReceive().Update(Arg.Any<InventoryItem>());
    }

    [Fact]
    public async Task ReserveStockAsync_WhenOneItemHasInsufficientStock_ShouldFailWithoutAllocatingAny()
    {
        // Arrange: Item 1 has sufficient stock (100), Item 2 only has 2 available
        var productId1 = ProductId.New();
        var item1 = InventoryItem.Create(productId1, "SKU-PUMP", initialQuantity: 100);

        var productId2 = ProductId.New();
        var item2 = InventoryItem.Create(productId2, "SKU-VALVE", initialQuantity: 2);

        _inventoryRepository.GetByProductIdsForUpdateAsync(
                Arg.Any<IReadOnlyCollection<ProductId>>(),
                Arg.Any<CancellationToken>())
            .Returns([item1, item2]);

        var request = new ReserveStockRequest(
            Guid.NewGuid(),
            [
                new ReservationItemDto(productId1.Value, "SKU-PUMP", 10),
                new ReservationItemDto(productId2.Value, "SKU-VALVE", 5) // Exceeds available stock (2)
            ]);

        // Act
        var result = await _module.ReserveStockAsync(request, CancellationToken.None);

        // Assert: Atomic reservation check ensures NO items were allocated
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("INSUFFICIENT_STOCK");

        item1.AllocatedQuantity.Should().Be(0); // Item 1 was NOT mutated
        item2.AllocatedQuantity.Should().Be(0);
        _inventoryRepository.DidNotReceive().Update(Arg.Any<InventoryItem>());
    }

    [Fact]
    public async Task ReserveStockAsync_ShouldRequestLocksInDeterministicAscendingOrder_ToPreventDeadlocks()
    {
        // Arrange: Prepare two GUIDs where guidHigh > guidLow
        var guidLow = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var guidHigh = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

        var pLow = new ProductId(guidLow);
        var pHigh = new ProductId(guidHigh);

        var itemLow = InventoryItem.Create(pLow, "SKU-LOW", initialQuantity: 50);
        var itemHigh = InventoryItem.Create(pHigh, "SKU-HIGH", initialQuantity: 50);

        IReadOnlyCollection<ProductId>? capturedProductIds = null;
        _inventoryRepository.GetByProductIdsForUpdateAsync(
                Arg.Do<IReadOnlyCollection<ProductId>>(ids => capturedProductIds = ids),
                Arg.Any<CancellationToken>())
            .Returns([itemLow, itemHigh]);

        // Request items in REVERSE order (High first, Low second)
        var request = new ReserveStockRequest(
            Guid.NewGuid(),
            [
                new ReservationItemDto(guidHigh, "SKU-HIGH", 5),
                new ReservationItemDto(guidLow, "SKU-LOW", 5)
            ]);

        // Act
        var result = await _module.ReserveStockAsync(request, CancellationToken.None);

        // Assert: Locks must be requested in deterministic ascending order regardless of input ordering
        result.IsSuccess.Should().BeTrue();
        capturedProductIds.Should().NotBeNull();
        capturedProductIds!.Select(p => p.Value).Should().ContainInOrder(guidLow, guidHigh);
    }
}
