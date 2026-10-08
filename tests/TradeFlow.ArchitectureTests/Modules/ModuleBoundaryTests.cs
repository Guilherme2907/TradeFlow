using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace TradeFlow.ArchitectureTests.Modules;

public class ModuleBoundaryTests
{
    private static readonly Assembly DomainAssembly = typeof(TradeFlow.Domain.Common.Entity<>).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(TradeFlow.Application.DependencyInjection).Assembly;

    private const string OrdersDomainNamespace = "TradeFlow.Domain.Orders";
    private const string InventoryDomainNamespace = "TradeFlow.Domain.Inventory";
    private const string OrdersApplicationNamespace = "TradeFlow.Application.Orders";
    private const string InventoryApplicationNamespace = "TradeFlow.Application.Inventory";
    private const string InventoryServicesNamespace = "TradeFlow.Application.Inventory.Services";

    [Fact]
    public void OrdersDomain_ShouldNotDependOn_InventoryDomain()
    {
        // Act: Orders domain aggregate must never depend on Inventory domain
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .ResideInNamespaceStartingWith(OrdersDomainNamespace)
            .ShouldNot()
            .HaveDependencyOn(InventoryDomainNamespace)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            $"Orders domain must not depend on Inventory domain. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void InventoryDomain_ShouldNotDependOn_OrdersDomain()
    {
        // Act: Inventory domain aggregate must never depend on Orders domain
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .ResideInNamespaceStartingWith(InventoryDomainNamespace)
            .ShouldNot()
            .HaveDependencyOn(OrdersDomainNamespace)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            $"Inventory domain must not depend on Orders domain. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void OrdersApplication_ShouldNotDependOn_InventoryDomain_Or_InternalInventoryServices()
    {
        // Act: ADR-0007: Orders application commands/handlers must never touch Inventory domain or internal services directly
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ResideInNamespaceStartingWith(OrdersApplicationNamespace)
            .ShouldNot()
            .HaveDependencyOnAny(
                InventoryDomainNamespace,
                InventoryServicesNamespace)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            $"Orders application must communicate with Inventory solely via gateways/contracts (ADR-0007). Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void InventoryApplication_ShouldNotDependOn_OrdersDomain_Or_OrdersApplication()
    {
        // Act: Inventory application module must remain completely independent of Orders
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ResideInNamespaceStartingWith(InventoryApplicationNamespace)
            .ShouldNot()
            .HaveDependencyOnAny(
                OrdersDomainNamespace,
                OrdersApplicationNamespace)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            $"Inventory application module must not depend on Orders. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
