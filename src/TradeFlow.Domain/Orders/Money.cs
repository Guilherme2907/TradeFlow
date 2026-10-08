using TradeFlow.Domain.Orders.Exceptions;

namespace TradeFlow.Domain.Orders;

/// <summary>
/// Immutable Value Object representing monetary values with strict currency validation.
/// </summary>
public readonly record struct Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency must be specified.", nameof(currency));

        if (amount < 0)
            throw OrderInvariantException.NegativeAmount(amount);

        Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = currency.Trim().ToUpperInvariant();
    }

    public static Money Create(decimal amount, string currency) => new(amount, currency);

    public static Money Zero(string currency) => new(0m, currency);

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator *(Money money, int factor)
    {
        if (factor < 0)
            throw new ArgumentOutOfRangeException(nameof(factor), "Multiplication factor cannot be negative.");
        return new Money(money.Amount * factor, money.Currency);
    }

    public static Money operator *(int factor, Money money) => money * factor;

    public static bool operator >(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount > right.Amount;
    }

    public static bool operator <(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount < right.Amount;
    }

    public static bool operator >=(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount >= right.Amount;
    }

    public static bool operator <=(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount <= right.Amount;
    }

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            throw OrderInvariantException.CurrencyMismatch(left.Currency, right.Currency);
    }

    public override string ToString() => $"{Amount:F2} {Currency}";
}
