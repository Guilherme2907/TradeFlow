using FluentAssertions;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Orders;
using TradeFlow.Domain.Orders.Exceptions;
using Xunit;

namespace TradeFlow.Domain.UnitTests.Orders;

public class OrderLineTests
{
    [Fact]
    public void OrderLine_WithValidSnapshot_ShouldComputeLineTotal()
    {
        // Arrange
        var customerId = CustomerId.New();
        var order = Order.Create(customerId, "USD");
        var productId = ProductId.New();
        var unitPrice = Money.Create(45.00m, "USD");

        // Act
        order.AddLine(productId, "SKU-PUMP-01", "Industrial Pump", unitPrice, quantity: 3);

        // Assert
        order.Lines.Should().HaveCount(1);
        var line = order.Lines.First();
        line.ProductId.Should().Be(productId);
        line.Sku.Should().Be("SKU-PUMP-01");
        line.ProductName.Should().Be("Industrial Pump");
        line.UnitPrice.Should().Be(unitPrice);
        line.Quantity.Should().Be(3);
        line.LineTotal.Should().Be(Money.Create(135.00m, "USD"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-50)]
    public void AddLine_WithZeroOrNegativeQuantity_ShouldThrowInvalidQuantityException(int invalidQuantity)
    {
        // Arrange
        var order = Order.Create(CustomerId.New(), "USD");

        // Act
        var act = () => order.AddLine(
            ProductId.New(),
            "SKU-001",
            "Widget",
            Money.Create(10m, "USD"),
            invalidQuantity);

        // Assert
        act.Should().Throw<OrderInvariantException>()
            .Which.Code.Should().Be("INVALID_LINE_QUANTITY");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddLine_WithInvalidSku_ShouldThrowArgumentException(string? invalidSku)
    {
        // Arrange
        var order = Order.Create(CustomerId.New(), "USD");

        // Act
        var act = () => order.AddLine(
            ProductId.New(),
            invalidSku!,
            "Widget",
            Money.Create(10m, "USD"),
            quantity: 1);

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}
