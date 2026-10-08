using FluentAssertions;
using NetArchTest.Rules;
using TradeFlow.Domain.Common;
using Xunit;

namespace TradeFlow.ArchitectureTests.Layers;

public class LayerDependencyTests
{
    private static readonly System.Reflection.Assembly DomainAssembly = typeof(Entity<>).Assembly;
    private static readonly System.Reflection.Assembly ApplicationAssembly = typeof(TradeFlow.Application.DependencyInjection).Assembly;
    private static readonly System.Reflection.Assembly InfrastructureAssembly = typeof(TradeFlow.Infrastructure.DependencyInjection).Assembly;
    private static readonly System.Reflection.Assembly ApiAssembly = typeof(Program).Assembly;

    private const string DomainNamespace = "TradeFlow.Domain";
    private const string ApplicationNamespace = "TradeFlow.Application";
    private const string InfrastructureNamespace = "TradeFlow.Infrastructure";
    private const string ApiNamespace = "TradeFlow.Api";

    [Fact]
    public void DomainLayer_ShouldNotHaveDependencyOn_OtherApplicationLayers()
    {
        // Act: Domain must remain pure and free from any inward or outward layer references
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                ApplicationNamespace,
                InfrastructureNamespace,
                ApiNamespace)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            $"Domain layer must not depend on other layers. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void ApplicationLayer_ShouldNotHaveDependencyOn_InfrastructureOrApiLayers()
    {
        // Act: Application can depend on Domain, but never on Infrastructure adapters or Presentation
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                InfrastructureNamespace,
                ApiNamespace)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            $"Application layer must not depend on Infrastructure or API. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void InfrastructureLayer_ShouldNotHaveDependencyOn_ApiLayer()
    {
        // Act: Infrastructure can implement Application/Domain gateways, but cannot depend on Presentation
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            $"Infrastructure layer must not depend on API layer. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void ApiEndpoints_ShouldNotHaveDirectDependencyOn_InfrastructureLayer()
    {
        // Act: API Endpoints must only interact with Application (MediatR/ISender), never directly with Infrastructure
        var result = Types.InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace("TradeFlow.Api.Endpoints")
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            $"API Endpoints must not directly reference Infrastructure types. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
