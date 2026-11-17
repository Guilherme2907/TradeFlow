using FluentAssertions;
using NSubstitute;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Common.Security;
using TradeFlow.Application.Orders.Queries.GetOrders;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Orders;
using Xunit;

namespace TradeFlow.Application.UnitTests.Orders;

public class GetOrdersQueryHandlerTests
{
    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly ICurrentUserContext _currentUser = Substitute.For<ICurrentUserContext>();
    private readonly GetOrdersQueryHandler _handler;

    public GetOrdersQueryHandlerTests()
    {
        _handler = new GetOrdersQueryHandler(_orderRepository, _currentUser);
    }

    [Fact]
    public async Task Handle_WhenUserIsTrader_ShouldScopeRepositoryFilterStrictlyToOwnCustomerId()
    {
        // Arrange
        var tenantAlpha = CustomerId.New();
        _currentUser.CustomerId.Returns(tenantAlpha.Value);
        _currentUser.IsInRole(Arg.Any<string>()).Returns(false);

        var alphaOrder = Order.Create(tenantAlpha, "USD");
        _orderRepository.ListAsync(Arg.Is<CustomerId?>(id => id == tenantAlpha), Arg.Any<CancellationToken>())
            .Returns([alphaOrder]);

        // Act
        var result = await _handler.Handle(new GetOrdersQuery(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(o => o.OrderId == alphaOrder.Id.Value && o.CustomerId == tenantAlpha.Value);
        await _orderRepository.Received(1).ListAsync(tenantAlpha, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTraderRequestsDifferentCustomerFilter_ShouldReturnForbidden_PreventingBolaIdor()
    {
        // Arrange
        var tenantAlpha = CustomerId.New();
        var tenantBeta = CustomerId.New();

        _currentUser.CustomerId.Returns(tenantAlpha.Value);
        _currentUser.IsInRole(Arg.Any<string>()).Returns(false);

        // Act
        var result = await _handler.Handle(new GetOrdersQuery(FilterCustomerId: tenantBeta.Value), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be("ORDER_TENANT_FORBIDDEN");
        await _orderRepository.DidNotReceive().ListAsync(Arg.Any<CustomerId?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenUserIsRiskManager_ShouldAllowCrossTenantListing()
    {
        // Arrange
        var tenantAlpha = CustomerId.New();
        var tenantBeta = CustomerId.New();

        _currentUser.CustomerId.Returns((Guid?)null);
        _currentUser.IsInRole(TradeFlowRoles.RiskManager).Returns(true);

        var orderA = Order.Create(tenantAlpha, "USD");
        var orderB = Order.Create(tenantBeta, "EUR");
        _orderRepository.ListAsync(null, Arg.Any<CancellationToken>())
            .Returns([orderA, orderB]);

        // Act
        var result = await _handler.Handle(new GetOrdersQuery(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        await _orderRepository.Received(1).ListAsync(null, Arg.Any<CancellationToken>());
    }
}
