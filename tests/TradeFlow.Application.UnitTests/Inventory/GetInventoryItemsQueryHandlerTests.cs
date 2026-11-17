using FluentAssertions;
using NSubstitute;
using TradeFlow.Application.Inventory.Queries.GetInventoryItems;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Inventory;
using Xunit;

namespace TradeFlow.Application.UnitTests.Inventory;

public class GetInventoryItemsQueryHandlerTests
{
    private readonly IInventoryRepository _inventoryRepository = Substitute.For<IInventoryRepository>();
    private readonly GetInventoryItemsQueryHandler _handler;

    public GetInventoryItemsQueryHandlerTests()
    {
        _handler = new GetInventoryItemsQueryHandler(_inventoryRepository);
    }

    [Fact]
    public async Task Handle_ShouldReturnAllInventoryItemsWithAvailableQuantities()
    {
        // Arrange
        var steel = InventoryItem.Create(ProductId.New(), "SKU-STEEL-001", 100);
        steel.Allocate(25);

        _inventoryRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([steel]);

        // Act
        var result = await _handler.Handle(new GetInventoryItemsQuery(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var item = result.Value.Should().ContainSingle().Subject;
        item.Sku.Should().Be("SKU-STEEL-001");
        item.QuantityOnHand.Should().Be(100);
        item.AllocatedQuantity.Should().Be(25);
        item.AvailableQuantity.Should().Be(75);
    }
}
