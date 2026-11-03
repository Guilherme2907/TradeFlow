using FluentAssertions;
using NSubstitute;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Common.Security;
using TradeFlow.Application.Orders.Gateways;
using TradeFlow.Application.Orders.Queries.GetOrderById;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Orders;
using Xunit;

namespace TradeFlow.Application.UnitTests.Orders;

public class GetOrderByIdQueryHandlerTests
{
    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly ICurrentUserContext _currentUser = Substitute.For<ICurrentUserContext>();
    private readonly GetOrderByIdQueryHandler _handler;

    public GetOrderByIdQueryHandlerTests()
    {
        _handler = new GetOrderByIdQueryHandler(_orderRepository, _currentUser);
    }

    [Fact]
    public async Task Handle_WhenOrderDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        _orderRepository.GetByIdAsync(Arg.Any<OrderId>(), Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var query = new GetOrderByIdQuery(orderId);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("ORDER_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenOrderBelongsToSameTenant_ShouldReturnOrderResponse()
    {
        // Arrange
        var customerId = CustomerId.New();
        _currentUser.CustomerId.Returns(customerId.Value);

        var order = Order.Create(customerId, "USD");
        order.AddLine(ProductId.New(), "SKU-VALVE-01", "Valve", Money.Create(150m, "USD"), 2);

        _orderRepository.GetByIdAsync(Arg.Is<OrderId>(id => id.Value == order.Id.Value), Arg.Any<CancellationToken>())
            .Returns(order);

        var query = new GetOrderByIdQuery(order.Id.Value, customerId.Value);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.OrderId.Should().Be(order.Id.Value);
        result.Value.CustomerId.Should().Be(customerId.Value);
        result.Value.Currency.Should().Be("USD");
        result.Value.TotalAmount.Should().Be(300m);
        result.Value.Lines.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_WhenOrderBelongsToDifferentTenant_AndUserIsTrader_ShouldReturnForbidden_PreventingBolaIdor()
    {
        // Arrange
        var tenantAlpha = CustomerId.New();
        var tenantBeta = CustomerId.New();

        _currentUser.CustomerId.Returns(tenantBeta.Value);
        _currentUser.IsInRole(TradeFlowRoles.Admin).Returns(false);
        _currentUser.IsInRole(TradeFlowRoles.ComplianceAuditor).Returns(false);
        _currentUser.IsInRole(TradeFlowRoles.RiskManager).Returns(false);

        var order = Order.Create(tenantAlpha, "USD");
        _orderRepository.GetByIdAsync(Arg.Is<OrderId>(id => id.Value == order.Id.Value), Arg.Any<CancellationToken>())
            .Returns(order);

        var query = new GetOrderByIdQuery(order.Id.Value, tenantBeta.Value);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be("ORDER_TENANT_FORBIDDEN");
    }

    [Fact]
    public async Task Handle_WhenOrderBelongsToDifferentTenant_AndUserIsComplianceAuditor_ShouldAllowRead()
    {
        // Arrange
        var tenantAlpha = CustomerId.New();
        _currentUser.CustomerId.Returns((Guid?)null);
        _currentUser.IsInRole(TradeFlowRoles.ComplianceAuditor).Returns(true);

        var order = Order.Create(tenantAlpha, "USD");
        _orderRepository.GetByIdAsync(Arg.Is<OrderId>(id => id.Value == order.Id.Value), Arg.Any<CancellationToken>())
            .Returns(order);

        var query = new GetOrderByIdQuery(order.Id.Value);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.OrderId.Should().Be(order.Id.Value);
        result.Value.CustomerId.Should().Be(tenantAlpha.Value);
    }
}
