namespace TradeFlow.Domain.Customers;

public readonly record struct CustomerAccountId(Guid Value)
{
    public static CustomerAccountId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct CustomerContractId(Guid Value)
{
    public static CustomerContractId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public enum CustomerTier
{
    Standard = 1,
    Gold = 2,
    Enterprise = 3
}
