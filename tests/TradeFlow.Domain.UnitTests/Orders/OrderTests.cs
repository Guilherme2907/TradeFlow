using FluentAssertions;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Orders;
using TradeFlow.Domain.Orders.Events;
using TradeFlow.Domain.Orders.Exceptions;
using Xunit;

namespace TradeFlow.Domain.UnitTests.Orders;

public class OrderTests
{
    private readonly CustomerId _customerId = CustomerId.New();

    [Fact]
    public void Create_ShouldInitializeInDraftStateWithZeroTotalAndRaiseOrderCreatedEvent()
    {
        // Act
        var order = Order.Create(_customerId, "USD");

        // Assert
        order.Status.Should().Be(OrderStatus.Draft);
        order.CustomerId.Should().Be(_customerId);
        order.Currency.Should().Be("USD");
        order.Lines.Should().BeEmpty();
        order.TotalAmount.Should().Be(Money.Zero("USD"));

        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderCreatedEvent>()
            .Which.OrderId.Should().Be(order.Id);
    }

    [Fact]
    public void AddLine_WithValidItem_ShouldIncreaseTotalAndRaiseLineAddedEvent()
    {
        // Arrange
        var order = Order.Create(_customerId, "USD");
        var productId = ProductId.New();
        var price = Money.Create(100m, "USD");

        // Act
        order.AddLine(productId, "SKU-VALVE-01", "High Pressure Valve", price, quantity: 2);

        // Assert
        order.Lines.Should().HaveCount(1);
        order.TotalAmount.Should().Be(Money.Create(200m, "USD"));

        order.DomainEvents.OfType<OrderLineAddedEvent>().Should().ContainSingle(e =>
            e.OrderId == order.Id &&
            e.ProductId == productId &&
            e.Quantity == 2);
    }

    [Fact]
    public void AddLine_WithExistingProductAndSamePrice_ShouldAccumulateQuantity()
    {
        // Arrange
        var order = Order.Create(_customerId, "USD");
        var productId = ProductId.New();
        var price = Money.Create(50m, "USD");

        // Act
        order.AddLine(productId, "SKU-PIPE-10", "Steel Pipe", price, quantity: 2);
        order.AddLine(productId, "SKU-PIPE-10", "Steel Pipe", price, quantity: 3);

        // Assert
        order.Lines.Should().HaveCount(1);
        var line = order.Lines.First();
        line.Quantity.Should().Be(5);
        order.TotalAmount.Should().Be(Money.Create(250m, "USD"));
    }

    [Fact]
    public void AddLine_WithCurrencyMismatch_ShouldThrowOrderInvariantException()
    {
        // Arrange
        var order = Order.Create(_customerId, "USD");

        // Act
        var act = () => order.AddLine(
            ProductId.New(),
            "SKU-FOREIGN",
            "Imported Cable",
            Money.Create(100m, "EUR"),
            quantity: 1);

        // Assert
        act.Should().Throw<OrderInvariantException>()
            .Which.Code.Should().Be("CURRENCY_MISMATCH");
    }

    [Fact]
    public void Submit_WhenOrderHasNoLines_ShouldThrowEmptyLinesException()
    {
        // Arrange
        var order = Order.Create(_customerId, "USD");

        // Act
        var act = () => order.Submit();

        // Assert
        act.Should().Throw<OrderInvariantException>()
            .Which.Code.Should().Be("ORDER_EMPTY_LINES");
    }

    [Fact]
    public void Submit_WhenBelowMinimumOrderValue_ShouldThrowMinimumOrderValueNotMetException()
    {
        // Arrange
        var minimumValue = Money.Create(500m, "USD");
        var order = Order.Create(_customerId, "USD", minimumOrderValue: minimumValue);
        order.AddLine(ProductId.New(), "SKU-LOW", "Low Value Item", Money.Create(100m, "USD"), quantity: 1);

        // Act
        var act = () => order.Submit();

        // Assert
        act.Should().Throw<OrderInvariantException>()
            .Which.Code.Should().Be("MINIMUM_ORDER_VALUE_NOT_MET");
    }

    [Fact]
    public void Submit_WhenValid_ShouldTransitionToSubmittedAndRaiseSubmittedEvent()
    {
        // Arrange
        var minimumValue = Money.Create(500m, "USD");
        var order = Order.Create(_customerId, "USD", minimumOrderValue: minimumValue);
        order.AddLine(ProductId.New(), "SKU-HIGH", "Generator", Money.Create(600m, "USD"), quantity: 1);
        var submitTime = DateTimeOffset.UtcNow;

        // Act
        order.Submit(submitTime);

        // Assert
        order.Status.Should().Be(OrderStatus.Submitted);
        order.SubmittedAt.Should().Be(submitTime);

        order.DomainEvents.OfType<OrderSubmittedEvent>().Should().ContainSingle(e =>
            e.OrderId == order.Id &&
            e.CustomerId == _customerId &&
            e.TotalAmount == Money.Create(600m, "USD"));
    }

    [Fact]
    public void AddLine_WhenOrderIsNotInDraft_ShouldThrowInvalidStateTransitionException()
    {
        // Arrange
        var order = Order.Create(_customerId, "USD");
        order.AddLine(ProductId.New(), "SKU-01", "Item", Money.Create(100m, "USD"), quantity: 1);
        order.Submit();

        // Act
        var act = () => order.AddLine(
            ProductId.New(),
            "SKU-02",
            "Item 2",
            Money.Create(50m, "USD"),
            quantity: 1);

        // Assert
        act.Should().Throw<OrderInvariantException>()
            .Which.Code.Should().Be("INVALID_ORDER_STATE_TRANSITION");
    }

    [Fact]
    public void Cancel_WhenOrderIsSubmitted_ShouldTransitionToCancelledAndSetReason()
    {
        // Arrange
        var order = Order.Create(_customerId, "USD");
        order.AddLine(ProductId.New(), "SKU-01", "Item", Money.Create(100m, "USD"), quantity: 1);
        order.Submit();

        // Act
        order.Cancel("Buyer requested cancellation due to budget freeze.");

        // Assert
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be("Buyer requested cancellation due to budget freeze.");
        order.CancelledAt.Should().NotBeNull();

        order.DomainEvents.OfType<OrderCancelledEvent>().Should().ContainSingle(e =>
            e.OrderId == order.Id &&
            e.Reason.Contains("budget freeze"));
    }

    [Fact]
    public void Cancel_WhenOrderIsAlreadyApproved_ShouldThrowInvalidStateTransitionException()
    {
        // Arrange
        var order = Order.Create(_customerId, "USD");
        order.AddLine(ProductId.New(), "SKU-01", "Item", Money.Create(100m, "USD"), quantity: 1);
        order.Submit();
        order.Approve();

        // Act
        var act = () => order.Cancel("Too late");

        // Assert
        act.Should().Throw<OrderInvariantException>()
            .Which.Code.Should().Be("INVALID_ORDER_STATE_TRANSITION");
    }

    [Fact]
    public void CreatedAt_ShouldBeInitializedOnCreation_AndUpdatedAtNullInitially()
    {
        // Act
        var before = DateTimeOffset.UtcNow;
        var order = Order.Create(_customerId, "USD");
        var after = DateTimeOffset.UtcNow;

        // Assert
        order.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        order.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void OrderMutations_ShouldUpdateUpdatedAtTimestamp()
    {
        // Arrange
        var order = Order.Create(_customerId, "USD");
        order.UpdatedAt.Should().BeNull();

        // Act 1: AddLine updates UpdatedAt
        order.AddLine(ProductId.New(), "SKU-01", "Item", Money.Create(100m, "USD"), quantity: 1);
        order.UpdatedAt.Should().NotBeNull();
        var afterAdd = order.UpdatedAt;

        // Act 2: Submit updates UpdatedAt
        order.Submit();
        order.UpdatedAt.Should().BeOnOrAfter(afterAdd!.Value);
    }
}
