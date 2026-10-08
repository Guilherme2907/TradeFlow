using FluentAssertions;
using NSubstitute;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Orders.Commands.AddOrderLine;
using TradeFlow.Application.Orders.Gateways;
using TradeFlow.Domain.Orders;
using Xunit;

namespace TradeFlow.Application.UnitTests.Orders;

public class AddOrderLineCommandHandlerTests
{
    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly AddOrderLineCommandHandler _handler;

    public AddOrderLineCommandHandlerTests()
    {
        _handler = new AddOrderLineCommandHandler(_orderRepository, _unitOfWork);
    }

    [Fact]
    public async Task Handle_WhenOrderDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        _orderRepository.GetByIdAsync(Arg.Any<OrderId>(), Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var command = new AddOrderLineCommand(
            orderId,
            Guid.NewGuid(),
            "SKU-01",
            "Industrial Valve",
            100m,
            2);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("ORDER_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenOrderExists_ShouldAddLineAndPersist()
    {
        // Arrange
        var order = Order.Create(CustomerId.New(), "USD");
        _orderRepository.GetByIdAsync(Arg.Is<OrderId>(id => id.Value == order.Id.Value), Arg.Any<CancellationToken>())
            .Returns(order);

        var productId = Guid.NewGuid();
        var command = new AddOrderLineCommand(
            order.Id.Value,
            productId,
            "SKU-VALVE-10",
            "Brass Valve",
            75.50m,
            3);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();

        order.Lines.Should().HaveCount(1);
        _orderRepository.Received(1).Update(order);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
