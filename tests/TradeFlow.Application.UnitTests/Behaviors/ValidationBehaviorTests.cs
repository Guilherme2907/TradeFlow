using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using NSubstitute;
using TradeFlow.Application.Common.Behaviors;
using TradeFlow.Application.Common.Models;
using Xunit;

namespace TradeFlow.Application.UnitTests.Behaviors;

public class ValidationBehaviorTests
{
    public sealed record SampleCommand(string Name) : IRequest<Result<string>>;

    [Fact]
    public async Task Handle_WhenValidationFails_ShouldReturnFailureResultWithoutInvokingNext()
    {
        // Arrange
        var validator = Substitute.For<IValidator<SampleCommand>>();
        var validationFailure = new ValidationFailure("Name", "Name cannot be empty.")
        {
            ErrorCode = "EMPTY_NAME"
        };

        validator.ValidateAsync(Arg.Any<ValidationContext<SampleCommand>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult([validationFailure]));

        var behavior = new ValidationBehavior<SampleCommand, Result<string>>([validator]);
        var next = Substitute.For<RequestHandlerDelegate<Result<string>>>();

        var command = new SampleCommand(string.Empty);

        // Act
        var result = await behavior.Handle(command, next, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("EMPTY_NAME");
        result.Error.Description.Should().Contain("Name cannot be empty.");

        await next.DidNotReceive().Invoke();
    }

    [Fact]
    public async Task Handle_WhenValidationPasses_ShouldInvokeNextDelegate()
    {
        // Arrange
        var validator = Substitute.For<IValidator<SampleCommand>>();
        validator.ValidateAsync(Arg.Any<ValidationContext<SampleCommand>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        var behavior = new ValidationBehavior<SampleCommand, Result<string>>([validator]);
        var next = Substitute.For<RequestHandlerDelegate<Result<string>>>();
        next.Invoke().Returns(Result.Success("OrderCreated"));

        var command = new SampleCommand("Valid Name");

        // Act
        var result = await behavior.Handle(command, next, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("OrderCreated");

        await next.Received(1).Invoke();
    }
}
