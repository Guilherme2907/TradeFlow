using FluentAssertions;
using NSubstitute;
using TradeFlow.Application.Catalog.Commands.CreateProduct;
using TradeFlow.Application.Catalog.Commands.UpdateProduct;
using TradeFlow.Application.Catalog.Queries.GetProducts;
using TradeFlow.Application.Catalog.Services;
using TradeFlow.Application.Common.Caching;
using TradeFlow.Application.Common.Models;
using TradeFlow.Domain.Catalog;
using TradeFlow.Domain.Common;
using Xunit;

namespace TradeFlow.Application.UnitTests.Catalog;

public class CatalogHandlersTests
{
    [Fact]
    public async Task CreateProduct_WhenSkuAlreadyExists_ShouldReturnConflictWithoutPersisting()
    {
        var repo = Substitute.For<IProductRepository>();
        var existing = Product.Create("SKU-STEEL-001", "Steel", "Ferrous", "Desc", 1250m, "USD");
        repo.GetBySkuAsync("SKU-STEEL-001", Arg.Any<CancellationToken>()).Returns(existing);

        var handler = new CreateProductCommandHandler(repo);
        var result = await handler.Handle(
            new CreateProductCommand("sku-steel-001", "Another Steel", "Ferrous", "Desc", 1300m, "USD"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("CATALOG_DUPLICATE_SKU");
        await repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateProduct_WhenProductExists_ShouldPersistAndEvictCacheKey()
    {
        var repo = Substitute.For<IProductRepository>();
        var cache = Substitute.For<ICacheService>();
        var product = Product.Create("SKU-COPPER-002", "Copper", "Non-Ferrous", "Desc", 890m, "USD");
        repo.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

        var handler = new UpdateProductCommandHandler(repo, cache);
        var result = await handler.Handle(
            new UpdateProductCommand(
                product.Id.Value,
                "Copper Cathode Grade A+",
                "Non-Ferrous",
                "Updated spec",
                950m,
                "USD",
                "Metric Ton",
                2,
                IsActive: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        product.UnitPrice.Should().Be(950m);
        product.MinimumOrderQuantity.Should().Be(2);
        await repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await cache.Received(1).RemoveAsync($"catalog:product:{product.Id.Value}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetProducts_ShouldReturnPagedResponseWithCalculatedTotalPages()
    {
        var repo = Substitute.For<IProductRepository>();
        var p1 = Product.Create("SKU-1", "Steel", "Ferrous", "Desc", 1000m, "USD");
        repo.SearchAsync("steel", "Ferrous", "USD", true, 1, 10, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<Product>)[p1], 25));

        var handler = new GetProductsQueryHandler(repo);
        var result = await handler.Handle(
            new GetProductsQuery("steel", "Ferrous", "USD", true, Page: 1, PageSize: 10),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle();
        result.Value.TotalCount.Should().Be(25);
        result.Value.TotalPages.Should().Be(3);
        result.Value.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public async Task CatalogModule_GetProductSnapshot_WhenMissing_ShouldReturnNotFound()
    {
        var repo = Substitute.For<IProductRepository>();
        repo.GetByIdAsync(Arg.Any<ProductId>(), Arg.Any<CancellationToken>()).Returns((Product?)null);

        var module = new CatalogModule(repo);
        var result = await module.GetProductSnapshotAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }
}
