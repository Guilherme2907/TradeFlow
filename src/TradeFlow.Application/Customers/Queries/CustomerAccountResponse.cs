using TradeFlow.Domain.Customers;

namespace TradeFlow.Application.Customers.Queries;

public sealed record CustomerContractResponse(
    Guid ContractId,
    string ContractNumber,
    string Title,
    string TermsText,
    decimal DiscountPercent,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset ExpiresAt,
    bool IsActive);

public sealed record CustomerAccountResponse(
    Guid CustomerId,
    string CompanyName,
    string TaxId,
    string Tier,
    decimal CreditLimit,
    string Currency,
    int PaymentTermsDays,
    bool IsActive,
    IReadOnlyList<CustomerContractResponse> Contracts,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

internal static class CustomerMappings
{
    public static CustomerAccountResponse ToResponse(this CustomerAccount account) => new(
        account.Id.Value,
        account.CompanyName,
        account.TaxId,
        account.Tier.ToString(),
        account.CreditLimit,
        account.Currency,
        account.PaymentTermsDays,
        account.IsActive,
        account.Contracts
            .OrderByDescending(c => c.EffectiveFrom)
            .Select(c => new CustomerContractResponse(
                c.Id.Value,
                c.ContractNumber,
                c.Title,
                c.TermsText,
                c.DiscountPercent,
                c.EffectiveFrom,
                c.ExpiresAt,
                c.IsActive))
            .ToList(),
        account.CreatedAt,
        account.UpdatedAt);
}
