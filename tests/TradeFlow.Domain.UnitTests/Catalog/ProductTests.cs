using FluentAssertions;
using TradeFlow.Domain.Catalog;
using TradeFlow.Domain.Catalog.Events;
using TradeFlow.Domain.Catalog.Exceptions;
using Xunit;

namespace TradeFlow.Domain.UnitTests.Catalog;

public class ProductTests
{
    [Fact]
    public void Create_WithValidParameters_ShouldNormalizeSkuAndCurrencyAndRaiseEvent()
    {
        var product = Product.Create(
            " sku-alum-7075 ",
            "Aerospace Aluminum 7075",
            "Non-Ferrous Metals",
            "High-strength aluminum plate.",
            3450.12345m,
            "usd",
            "Metric Ton",
            minimumOrderQuantity: 2);

        product.Sku.Should().Be("SKU-ALUM-7075");
        product.Currency.Should().Be("USD");
        product.UnitPrice.Should().Be(3450.1235m);
        product.MinimumOrderQuantity.Should().Be(2);
        product.IsActive.Should().BeTrue();
        product.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ProductCreatedDomainEvent>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Create_WithNonPositivePrice_ShouldThrowCatalogInvariantException(decimal invalidPrice)
    {
        var act = () => Product.Create(
            "SKU-TEST-001",
            "Test Product",
            "Metals",
            "Description",
            invalidPrice,
            "USD");

        act.Should().Throw<CatalogInvariantException>()
            .Which.Code.Should().Be("CATALOG_INVALID_PRICE");
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("US1")]
    public void Create_WithInvalidCurrency_ShouldThrowCatalogInvariantException(string invalidCurrency)
    {
        var act = () => Product.Create(
            "SKU-TEST-001",
            "Test Product",
            "Metals",
            "Description",
            100m,
            invalidCurrency);

        act.Should().Throw<CatalogInvariantException>()
            .Which.Code.Should().Be("CATALOG_INVALID_CURRENCY");
    }

    [Fact]
    public void UpdatePrice_WhenPriceChanges_ShouldUpdatePriceAndRaiseDomainEvent()
    {
        var product = Product.Create("SKU-STEEL-001", "Steel Coil", "Ferrous", "Desc", 1200m, "USD");
        product.ClearDomainEvents();

        product.UpdatePrice(1350m, "USD");

        product.UnitPrice.Should().Be(1350m);
        product.UpdatedAt.Should().NotBeNull();
        product.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ProductPriceChangedDomainEvent>()
            .Which.PreviousPrice.Should().Be(1200m);
    }

    [Fact]
    public void DeactivateAndActivate_ShouldToggleActiveStatus()
    {
        var product = Product.Create("SKU-STEEL-001", "Steel Coil", "Ferrous", "Desc", 1200m, "USD");

        product.Deactivate();
        product.IsActive.Should().BeFalse();

        product.Activate();
        product.IsActive.Should().BeTrue();
    }
}
