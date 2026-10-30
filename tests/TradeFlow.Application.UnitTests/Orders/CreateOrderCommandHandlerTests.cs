using FluentAssertions;
using NSubstitute;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Common.Security;
using TradeFlow.Application.Orders.Commands.CreateOrder;
using TradeFlow.Application.Orders.Gateways;
using TradeFlow.Domain.Orders;
using Xunit;

namespace TradeFlow.Application.UnitTests.Orders;

public class CreateOrderCommandHandlerTests
{
    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserContext _currentUser = Substitute.For<ICurrentUserContext>();
    private readonly CreateOrderCommandHandler _handler;

    public CreateOrderCommandHandlerTests()
    {
        _handler = new CreateOrderCommandHandler(_orderRepository, _unitOfWork, _currentUser);
    }

    [Fact]
    public async Task Handle_WithValidAuthenticatedTenant_ShouldPersistOrderWithCustomerIdFromTokenAndReturnId()
    {
        // Arrange
        var tokenCustomerId = Guid.NewGuid();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.CustomerId.Returns(tokenCustomerId);

        var command = new CreateOrderCommand("USD", MinimumOrderValue: 200m);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();

        await _orderRepository.Received(1).AddAsync(
            Arg.Is<Order>(o => o.CustomerId.Value == tokenCustomerId && o.Currency == "USD"),
            Arg.Any<CancellationToken>());

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenCustomerIdClaimIsMissing_ShouldReturnForbiddenAndNotPersist()
    {
        // Arrange
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.CustomerId.Returns((Guid?)null);

        var command = new CreateOrderCommand("USD");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be("CUSTOMER_TENANT_REQUIRED");

        await _orderRepository.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
