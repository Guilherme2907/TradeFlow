using TradeFlow.Domain.Common;

namespace TradeFlow.Domain.Orders.Exceptions;

public class OrderInvariantException : DomainException
{
    public OrderInvariantException(string code, string message) : base(code, message)
    {
    }

    public static OrderInvariantException EmptyLines() =>
        new("ORDER_EMPTY_LINES", "An order must contain at least one line item to be submitted.");

    public static OrderInvariantException InvalidQuantity(int quantity) =>
        new("INVALID_LINE_QUANTITY", $"Line quantity must be greater than zero. Received: {quantity}.");

    public static OrderInvariantException NegativeAmount(decimal amount) =>
        new("NEGATIVE_MONEY_AMOUNT", $"Money amount cannot be negative. Received: {amount}.");

    public static OrderInvariantException CurrencyMismatch(string currency1, string currency2) =>
        new("CURRENCY_MISMATCH", $"Cannot operate on different currencies: '{currency1}' and '{currency2}'.");

    public static OrderInvariantException InvalidStateTransition(OrderStatus current, OrderStatus target) =>
        new("INVALID_ORDER_STATE_TRANSITION", $"Cannot transition order from state '{current}' to '{target}'.");

    public static OrderInvariantException MinimumOrderValueNotMet(decimal total, decimal minimum, string currency) =>
        new("MINIMUM_ORDER_VALUE_NOT_MET", $"Order total of {total:F2} {currency} does not meet the B2B minimum required of {minimum:F2} {currency}.");

    public static OrderInvariantException LineNotFound(OrderLineId lineId) =>
        new("ORDER_LINE_NOT_FOUND", $"Order line with ID '{lineId.Value}' was not found in the order.");
}
