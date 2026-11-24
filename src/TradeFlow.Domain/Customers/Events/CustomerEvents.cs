using TradeFlow.Domain.Common;

namespace TradeFlow.Domain.Customers.Events;

public sealed record CustomerAccountCreatedDomainEvent(
    CustomerAccountId CustomerId,
    string CompanyName,
    CustomerTier Tier,
    decimal CreditLimit,
    string Currency,
    DateTimeOffset OccurredOn) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
}

public sealed record CustomerContractAddedDomainEvent(
    CustomerAccountId CustomerId,
    CustomerContractId ContractId,
    string ContractNumber,
    string Title,
    DateTimeOffset OccurredOn) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
}
