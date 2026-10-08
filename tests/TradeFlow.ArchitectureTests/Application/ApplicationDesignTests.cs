using System.Reflection;
using FluentAssertions;
using MediatR;
using NetArchTest.Rules;
using Xunit;

namespace TradeFlow.ArchitectureTests.Application;

public class ApplicationDesignTests
{
    private static readonly Assembly ApplicationAssembly = typeof(TradeFlow.Application.DependencyInjection).Assembly;

    [Fact]
    public void Commands_ShouldBeSealed_AndEndWithCommand()
    {
        // Act: All commands in Application.Orders.Commands must be sealed and end with 'Command'
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ResideInNamespaceEndingWith("Commands")
            .And()
            .AreClasses()
            .Should()
            .BeSealed()
            .And()
            .HaveNameEndingWith("Command")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            $"Commands must be sealed records/classes ending with 'Command'. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void CommandHandlers_ShouldBeSealed_AndEndWithCommandHandler()
    {
        // Act: Handlers must be sealed classes ending with 'CommandHandler' or 'Handler'
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IRequestHandler<,>))
            .Should()
            .BeSealed()
            .And()
            .HaveNameEndingWith("Handler")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            $"Command handlers must be sealed classes ending with 'Handler'. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void CommandHandlers_ShouldNotDependOn_ISenderOrIMediator_AntiBackdoorRule()
    {
        // Act: Enforce ADR-0003: Handlers must never orchestrate other handlers via mediator
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IRequestHandler<,>))
            .ShouldNot()
            .HaveDependencyOnAny("MediatR.ISender", "MediatR.IMediator")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            $"Anti-Backdoor Rule violation: Handlers must never inject ISender or IMediator. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
