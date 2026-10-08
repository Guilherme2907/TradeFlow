using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using TradeFlow.Domain.Common;
using Xunit;

namespace TradeFlow.ArchitectureTests.Domain;

public class DomainEncapsulationTests
{
    private static readonly Assembly DomainAssembly = typeof(Entity<>).Assembly;

    [Fact]
    public void DomainEvents_ShouldImplementIDomainEvent_AndBeSealed()
    {
        // Act: All domain events must implement IDomainEvent and be sealed
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .ImplementInterface(typeof(IDomainEvent))
            .Should()
            .BeSealed()
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            $"Domain events must be sealed. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void DomainEntities_ShouldNotHavePublicSetters()
    {
        // Arrange: Find all types inheriting from Entity<> or AggregateRoot<>
        var entityTypes = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(Entity<>))
            .GetTypes();

        var failingProperties = new List<string>();

        // Act: Verify every entity property has a non-public setter
        foreach (var type in entityTypes)
        {
            var publicSetters = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.SetMethod is not null && p.SetMethod.IsPublic)
                .Select(p => $"{type.Name}.{p.Name}");

            failingProperties.AddRange(publicSetters);
        }

        // Assert
        failingProperties.Should().BeEmpty(
            $"Domain entity properties must not have public setters. Failing properties: {string.Join(", ", failingProperties)}");
    }

    [Fact]
    public void DomainEntities_ShouldHavePrivateOrProtectedParameterlessConstructor_ForEFCore()
    {
        // Arrange: EF Core requires a parameterless constructor (can be private or protected)
        var entityTypes = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(Entity<>))
            .And()
            .AreNotAbstract()
            .GetTypes();

        var failingTypes = new List<string>();

        // Act
        foreach (var type in entityTypes)
        {
            var hasParameterlessCtor = type.GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Any(c => c.GetParameters().Length == 0);

            if (!hasParameterlessCtor)
            {
                failingTypes.Add(type.Name);
            }
        }

        // Assert
        failingTypes.Should().BeEmpty(
            $"Domain entities must have a parameterless constructor for EF Core rehydration. Failing types: {string.Join(", ", failingTypes)}");
    }
}
