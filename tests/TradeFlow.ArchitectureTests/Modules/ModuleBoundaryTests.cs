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
    private const string CatalogDomainNamespace = "TradeFlow.Domain.Catalog";
    private const string CustomersDomainNamespace = "TradeFlow.Domain.Customers";
    private const string OrdersApplicationNamespace = "TradeFlow.Application.Orders";
    private const string InventoryApplicationNamespace = "TradeFlow.Application.Inventory";
    private const string InventoryServicesNamespace = "TradeFlow.Application.Inventory.Services";
    private const string CatalogApplicationNamespace = "TradeFlow.Application.Catalog";
    private const string CatalogServicesNamespace = "TradeFlow.Application.Catalog.Services";

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

    private static readonly Assembly InfrastructureAssembly = typeof(TradeFlow.Infrastructure.DependencyInjection).Assembly;

    [Fact]
    public void OrdersInfrastructure_ShouldNotDependOn_InventoryPersistenceOrDomain()
    {
        // ADR-0010: module DbContexts/repositories may only touch their own module's entities (no cross-module joins)
        var result = Types.InAssembly(InfrastructureAssembly)
            .That()
            .ResideInNamespaceStartingWith("TradeFlow.Infrastructure.Modules.Orders.Persistence")
            .ShouldNot()
            .HaveDependencyOnAny(
                InventoryDomainNamespace,
                "TradeFlow.Infrastructure.Modules.Inventory")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Orders persistence must not reference Inventory. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void InventoryInfrastructure_ShouldNotDependOn_OrdersPersistenceOrDomain()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That()
            .ResideInNamespaceStartingWith("TradeFlow.Infrastructure.Modules.Inventory")
            .ShouldNot()
            .HaveDependencyOnAny(
                OrdersDomainNamespace,
                "TradeFlow.Infrastructure.Modules.Orders")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Inventory persistence must not reference Orders. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void CatalogDomain_ShouldNotDependOn_OrdersOrInventoryDomain()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .ResideInNamespaceStartingWith(CatalogDomainNamespace)
            .ShouldNot()
            .HaveDependencyOnAny(OrdersDomainNamespace, InventoryDomainNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Catalog domain must remain isolated from Orders and Inventory domains. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void OrdersApplication_ShouldNotDependOn_CatalogDomain_Or_InternalCatalogServices()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ResideInNamespaceStartingWith(OrdersApplicationNamespace)
            .ShouldNot()
            .HaveDependencyOnAny(CatalogDomainNamespace, CatalogServicesNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Orders application must communicate with Catalog solely via gateways/contracts (ADR-0007). Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void CatalogInfrastructure_ShouldNotDependOn_OrdersOrInventoryPersistence()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That()
            .ResideInNamespaceStartingWith("TradeFlow.Infrastructure.Modules.Catalog")
            .ShouldNot()
            .HaveDependencyOnAny(
                OrdersDomainNamespace,
                InventoryDomainNamespace,
                "TradeFlow.Infrastructure.Modules.Orders",
                "TradeFlow.Infrastructure.Modules.Inventory")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Catalog persistence must not reference Orders or Inventory. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void CustomersDomain_ShouldNotDependOn_OtherModuleDomains()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .ResideInNamespaceStartingWith(CustomersDomainNamespace)
            .ShouldNot()
            .HaveDependencyOnAny(OrdersDomainNamespace, InventoryDomainNamespace, CatalogDomainNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Customers domain must remain isolated from other module domains. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void CustomersInfrastructure_ShouldNotDependOn_OtherModulePersistence()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That()
            .ResideInNamespaceStartingWith("TradeFlow.Infrastructure.Modules.Customers")
            .ShouldNot()
            .HaveDependencyOnAny(
                OrdersDomainNamespace,
                InventoryDomainNamespace,
                CatalogDomainNamespace,
                "TradeFlow.Infrastructure.Modules.Orders",
                "TradeFlow.Infrastructure.Modules.Inventory",
                "TradeFlow.Infrastructure.Modules.Catalog")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Customers persistence must not reference other modules. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void ModuleDbContexts_ShouldImplement_IModuleDbContext()
    {
        // Guarantees every module context participates in the shared UnitOfWork transaction
        var result = Types.InAssembly(InfrastructureAssembly)
            .That()
            .Inherit(typeof(Microsoft.EntityFrameworkCore.DbContext))
            .Should()
            .ImplementInterface(typeof(TradeFlow.Infrastructure.Persistence.IModuleDbContext))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Every DbContext must implement IModuleDbContext. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void AiApplication_ShouldNotDependOn_ModuleDomains_Or_InternalModuleServices()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ResideInNamespaceStartingWith("TradeFlow.Application.AI")
            .ShouldNot()
            .HaveDependencyOnAny(
                OrdersDomainNamespace,
                InventoryDomainNamespace,
                CatalogDomainNamespace,
                CustomersDomainNamespace,
                InventoryServicesNamespace,
                CatalogServicesNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"TradeFlow.Application.AI must interact with other modules solely via public contracts (ADR-0007 / ADR-0020). Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void AiInfrastructure_ShouldNotDependOn_OtherModulePersistenceOrDomains()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That()
            .ResideInNamespaceStartingWith("TradeFlow.Infrastructure.Modules.AI")
            .ShouldNot()
            .HaveDependencyOnAny(
                OrdersDomainNamespace,
                InventoryDomainNamespace,
                CatalogDomainNamespace,
                CustomersDomainNamespace,
                "TradeFlow.Infrastructure.Modules.Orders",
                "TradeFlow.Infrastructure.Modules.Inventory",
                "TradeFlow.Infrastructure.Modules.Catalog",
                "TradeFlow.Infrastructure.Modules.Customers")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"AI infrastructure adapters must remain decoupled from domain persistence. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
