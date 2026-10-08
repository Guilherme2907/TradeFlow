using FluentAssertions;
using TradeFlow.Domain.Orders;
using TradeFlow.Domain.Orders.Exceptions;
using Xunit;

namespace TradeFlow.Domain.UnitTests.Orders;

public class MoneyTests
{
    [Fact]
    public void Create_WithValidAmountAndCurrency_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        var money = Money.Create(150.755m, "usd");

        // Assert
        money.Amount.Should().Be(150.76m); // Rounded to 2 decimal places
        money.Currency.Should().Be("USD"); // Normalized to uppercase
    }

    [Fact]
    public void Create_WithNegativeAmount_ShouldThrowOrderInvariantException()
    {
        // Act
        var act = () => Money.Create(-10m, "USD");

        // Assert
        act.Should().Throw<OrderInvariantException>()
            .Which.Code.Should().Be("NEGATIVE_MONEY_AMOUNT");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyCurrency_ShouldThrowArgumentException(string? invalidCurrency)
    {
        // Act
        var act = () => Money.Create(100m, invalidCurrency!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Addition_WithSameCurrency_ShouldReturnSum()
    {
        // Arrange
        var a = Money.Create(100.50m, "USD");
        var b = Money.Create(49.50m, "USD");

        // Act
        var result = a + b;

        // Assert
        result.Amount.Should().Be(150.00m);
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public void Addition_WithDifferentCurrencies_ShouldThrowCurrencyMismatchException()
    {
        // Arrange
        var usd = Money.Create(100m, "USD");
        var eur = Money.Create(100m, "EUR");

        // Act
        var act = () => _ = usd + eur;

        // Assert
        act.Should().Throw<OrderInvariantException>()
            .Which.Code.Should().Be("CURRENCY_MISMATCH");
    }

    [Fact]
    public void Multiplication_WithPositiveFactor_ShouldCalculateTotal()
    {
        // Arrange
        var unitPrice = Money.Create(25.50m, "USD");

        // Act
        var total = unitPrice * 4;

        // Assert
        total.Amount.Should().Be(102.00m);
        total.Currency.Should().Be("USD");
    }

    [Fact]
    public void Comparison_ShouldEvaluateAccurately()
    {
        // Arrange
        var smaller = Money.Create(50m, "USD");
        var larger = Money.Create(100m, "USD");

        // Assert
        (smaller < larger).Should().BeTrue();
        (larger > smaller).Should().BeTrue();
        (smaller <= larger).Should().BeTrue();
        (larger >= smaller).Should().BeTrue();
    }
}
