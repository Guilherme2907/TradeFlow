using FluentAssertions;
using NSubstitute;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Orders.Commands.SubmitOrder;
using TradeFlow.Application.Orders.Gateways;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Orders;
using TradeFlow.Domain.Orders.Exceptions;
using Xunit;

namespace TradeFlow.Application.UnitTests.Orders;

public class SubmitOrderCommandHandlerTests
{
    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly IInventoryReservationService _inventoryService = Substitute.For<IInventoryReservationService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly SubmitOrderCommandHandler _handler;

    public SubmitOrderCommandHandlerTests()
    {
        _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task<Result>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<Task<Result>>>()());

        _handler = new SubmitOrderCommandHandler(_orderRepository, _inventoryService, _unitOfWork);
    }

    [Fact]
    public async Task Handle_WhenOrderDoesNotExist_ShouldReturnNotFound()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        _orderRepository.GetByIdAsync(Arg.Any<OrderId>(), Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var command = new SubmitOrderCommand(orderId);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("ORDER_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenOrderHasNoLines_ShouldThrowOrderInvariantExceptionAndNotCallInventory()
    {
        // Arrange
        var order = Order.Create(CustomerId.New(), "USD");
        _orderRepository.GetByIdAsync(Arg.Is<OrderId>(id => id.Value == order.Id.Value), Arg.Any<CancellationToken>())
            .Returns(order);

        var command = new SubmitOrderCommand(order.Id.Value);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<OrderInvariantException>();
        exception.Which.Code.Should().Be("ORDER_EMPTY_LINES");

        await _inventoryService.DidNotReceive().ReserveStockAsync(
            Arg.Any<OrderId>(),
            Arg.Any<IReadOnlyCollection<ReservationItem>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenInventoryReservationFails_ShouldReturnFailureAndNotCommitOrder()
    {
        // Arrange
        var order = Order.Create(CustomerId.New(), "USD");
        order.AddLine(ProductId.New(), "SKU-PUMP", "Pump", Money.Create(100m, "USD"), quantity: 5);

        _orderRepository.GetByIdAsync(Arg.Is<OrderId>(id => id.Value == order.Id.Value), Arg.Any<CancellationToken>())
            .Returns(order);

        var stockError = Error.Conflict("INSUFFICIENT_STOCK", "Only 2 units available in warehouse.");
        _inventoryService.ReserveStockAsync(
            Arg.Any<OrderId>(),
            Arg.Any<IReadOnlyCollection<ReservationItem>>(),
            Arg.Any<CancellationToken>())
            .Returns(Result.Failure(stockError));

        var command = new SubmitOrderCommand(order.Id.Value);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("INSUFFICIENT_STOCK");
        order.Status.Should().Be(OrderStatus.Draft); // Order remains in Draft / uncommitted

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenOrderValidAndInventoryReserved_ShouldSubmitOrderAndPersist()
    {
        // Arrange
        var order = Order.Create(CustomerId.New(), "USD");
        order.AddLine(ProductId.New(), "SKU-PUMP", "Pump", Money.Create(100m, "USD"), quantity: 5);

        _orderRepository.GetByIdAsync(Arg.Is<OrderId>(id => id.Value == order.Id.Value), Arg.Any<CancellationToken>())
            .Returns(order);

        _inventoryService.ReserveStockAsync(
            Arg.Any<OrderId>(),
            Arg.Any<IReadOnlyCollection<ReservationItem>>(),
            Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var command = new SubmitOrderCommand(order.Id.Value);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Submitted);

        _orderRepository.Received(1).Update(order);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
