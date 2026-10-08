using TradeFlow.Domain.Common;
using TradeFlow.Domain.Orders.Events;
using TradeFlow.Domain.Orders.Exceptions;

namespace TradeFlow.Domain.Orders;

/// <summary>
/// Aggregate Root representing a B2B Purchase Order.
/// Encapsulates all invariants regarding line items, commercial snapshots,
/// minimum order values, and lifecycle state transitions.
/// </summary>
public class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderLine> _lines = [];

    public CustomerId CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public Money? MinimumOrderValue { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();

    public Money TotalAmount => _lines.Count == 0
        ? Money.Zero(Currency)
        : _lines.Aggregate(Money.Zero(Currency), (acc, line) => acc + line.LineTotal);

    // Required by EF Core
    private Order() { }

    private Order(
        OrderId id,
        CustomerId customerId,
        string currency,
        Money? minimumOrderValue,
        DateTimeOffset createdAt) : base(id, createdAt)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Order currency must be provided.", nameof(currency));

        string normalizedCurrency = currency.Trim().ToUpperInvariant();

        if (minimumOrderValue.HasValue && minimumOrderValue.Value.Currency != normalizedCurrency)
            throw OrderInvariantException.CurrencyMismatch(minimumOrderValue.Value.Currency, normalizedCurrency);

        CustomerId = customerId;
        Currency = normalizedCurrency;
        Status = OrderStatus.Draft;
        MinimumOrderValue = minimumOrderValue;

        RaiseDomainEvent(new OrderCreatedEvent(Id, CustomerId, Currency, CreatedAt));
    }

    public static Order Create(
        CustomerId customerId,
        string currency,
        Money? minimumOrderValue = null,
        DateTimeOffset? createdAt = null)
    {
        return new Order(
            OrderId.New(),
            customerId,
            currency,
            minimumOrderValue,
            createdAt ?? DateTimeOffset.UtcNow);
    }

    public OrderLineId AddLine(
        ProductId productId,
        string sku,
        string productName,
        Money unitPrice,
        int quantity)
    {
        EnsureStatus(OrderStatus.Draft, "add line items to");

        if (unitPrice.Currency != Currency)
            throw OrderInvariantException.CurrencyMismatch(unitPrice.Currency, Currency);

        // If the same SKU already exists with the identical unit price, accumulate quantity
        var existingLine = _lines.FirstOrDefault(l => l.ProductId == productId && l.UnitPrice == unitPrice);
        if (existingLine is not null)
        {
            existingLine.UpdateQuantity(existingLine.Quantity + quantity);
            Touch();
            RaiseDomainEvent(new OrderLineAddedEvent(Id, existingLine.Id, productId, sku, unitPrice, existingLine.Quantity, DateTimeOffset.UtcNow));
            return existingLine.Id;
        }

        var lineId = OrderLineId.New();
        var newLine = new OrderLine(lineId, productId, sku, productName, unitPrice, quantity);
        _lines.Add(newLine);
        Touch();

        RaiseDomainEvent(new OrderLineAddedEvent(Id, lineId, productId, sku, unitPrice, quantity, DateTimeOffset.UtcNow));
        return lineId;
    }

    public void RemoveLine(OrderLineId lineId)
    {
        EnsureStatus(OrderStatus.Draft, "remove line items from");

        var line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
            throw OrderInvariantException.LineNotFound(lineId);

        _lines.Remove(line);
        Touch();
    }

    public void ValidateCanSubmit()
    {
        EnsureStatus(OrderStatus.Draft, "submit");

        if (_lines.Count == 0)
            throw OrderInvariantException.EmptyLines();

        if (MinimumOrderValue.HasValue && TotalAmount < MinimumOrderValue.Value)
        {
            throw OrderInvariantException.MinimumOrderValueNotMet(
                TotalAmount.Amount,
                MinimumOrderValue.Value.Amount,
                Currency);
        }
    }

    public void Submit(DateTimeOffset? submittedAt = null)
    {
        ValidateCanSubmit();

        Status = OrderStatus.Submitted;
        SubmittedAt = submittedAt ?? DateTimeOffset.UtcNow;
        Touch(SubmittedAt);

        RaiseDomainEvent(new OrderSubmittedEvent(Id, CustomerId, TotalAmount, SubmittedAt.Value));
    }

    public void Approve()
    {
        EnsureStatus(OrderStatus.Submitted, "approve");

        Status = OrderStatus.Approved;
        Touch();
        RaiseDomainEvent(new OrderApprovedEvent(Id, DateTimeOffset.UtcNow));
    }

    public void Cancel(string reason, DateTimeOffset? cancelledAt = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Cancellation reason must be provided.", nameof(reason));

        if (Status is OrderStatus.Approved or OrderStatus.Cancelled or OrderStatus.Rejected)
            throw OrderInvariantException.InvalidStateTransition(Status, OrderStatus.Cancelled);

        Status = OrderStatus.Cancelled;
        CancelledAt = cancelledAt ?? DateTimeOffset.UtcNow;
        CancellationReason = reason.Trim();
        Touch(CancelledAt);

        RaiseDomainEvent(new OrderCancelledEvent(Id, CancellationReason, CancelledAt.Value));
    }

    private void EnsureStatus(OrderStatus expected, string action)
    {
        if (Status != expected)
            throw OrderInvariantException.InvalidStateTransition(Status, expected);
    }
}
