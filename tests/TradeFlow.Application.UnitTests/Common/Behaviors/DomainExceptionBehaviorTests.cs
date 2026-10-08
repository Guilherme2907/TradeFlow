using FluentAssertions;
using MediatR;
using TradeFlow.Application.Common.Behaviors;
using TradeFlow.Application.Common.Models;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Orders;
using TradeFlow.Domain.Orders.Exceptions;
using Xunit;

namespace TradeFlow.Application.UnitTests.Common.Behaviors;

public class DomainExceptionBehaviorTests
{
    private sealed record TestResultCommand : IRequest<Result>;
    private sealed record TestGenericResultCommand : IRequest<Result<Guid>>;

    [Fact]
    public async Task Handle_WhenDomainExceptionThrownOnResult_ShouldReturnValidationFailure()
    {
        // Arrange
        var behavior = new DomainExceptionBehavior<TestResultCommand, Result>();
        RequestHandlerDelegate<Result> next = _ => Task.FromException<Result>(OrderInvariantException.EmptyLines());

        // Act
        var result = await behavior.Handle(new TestResultCommand(), next, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("ORDER_EMPTY_LINES");
        result.Error.Description.Should().Be("An order must contain at least one line item to be submitted.");
    }

    [Fact]
    public async Task Handle_WhenDomainExceptionThrownOnGenericResult_ShouldReturnGenericValidationFailure()
    {
        // Arrange
        var behavior = new DomainExceptionBehavior<TestGenericResultCommand, Result<Guid>>();
        RequestHandlerDelegate<Result<Guid>> next = _ => Task.FromException<Result<Guid>>(
            OrderInvariantException.InvalidStateTransition(OrderStatus.Submitted, OrderStatus.Draft));

        // Act
        var result = await behavior.Handle(new TestGenericResultCommand(), next, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("INVALID_ORDER_STATE_TRANSITION");
    }

    [Fact]
    public async Task Handle_WhenTechnicalExceptionThrown_ShouldNotCatchAndRethrow()
    {
        // Arrange
        var behavior = new DomainExceptionBehavior<TestResultCommand, Result>();
        RequestHandlerDelegate<Result> next = _ => Task.FromException<Result>(new InvalidOperationException("DB connection timeout"));

        // Act
        var act = () => behavior.Handle(new TestResultCommand(), next, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("DB connection timeout");
    }

    [Fact]
    public async Task Handle_WhenSuccess_ShouldReturnResponseWithoutInterference()
    {
        // Arrange
        var behavior = new DomainExceptionBehavior<TestResultCommand, Result>();
        RequestHandlerDelegate<Result> next = _ => Task.FromResult(Result.Success());

        // Act
        var result = await behavior.Handle(new TestResultCommand(), next, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }
}
