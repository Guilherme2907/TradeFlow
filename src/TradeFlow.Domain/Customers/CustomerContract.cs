using TradeFlow.Domain.Common;
using TradeFlow.Domain.Customers.Exceptions;

namespace TradeFlow.Domain.Customers;

/// <summary>
/// Entity owned by <see cref="CustomerAccount"/> representing a tenant-confidential commercial B2B contract
/// (pricing discounts, cancellation clauses, and SLA terms — primary source for multi-tenant RAG in Phase 12).
/// </summary>
public class CustomerContract : Entity<CustomerContractId>
{
    public CustomerAccountId CustomerAccountId { get; private set; }
    public string ContractNumber { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string TermsText { get; private set; } = string.Empty;
    public decimal DiscountPercent { get; private set; }
    public DateTimeOffset EffectiveFrom { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public bool IsActive { get; private set; }

    private CustomerContract() { }

    internal CustomerContract(
        CustomerContractId id,
        CustomerAccountId customerAccountId,
        string contractNumber,
        string title,
        string termsText,
        decimal discountPercent,
        DateTimeOffset effectiveFrom,
        DateTimeOffset expiresAt) : base(id)
    {
        if (string.IsNullOrWhiteSpace(contractNumber) || contractNumber.Trim().Length > 80)
            throw CustomerInvariantException.InvalidContractNumber();
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
            throw CustomerInvariantException.InvalidContractTitle();
        if (string.IsNullOrWhiteSpace(termsText) || termsText.Trim().Length > 4000)
            throw CustomerInvariantException.InvalidContractTerms();
        if (discountPercent < 0m || discountPercent > 50m)
            throw CustomerInvariantException.InvalidDiscount(discountPercent);
        if (expiresAt <= effectiveFrom)
            throw CustomerInvariantException.InvalidContractDateRange();

        CustomerAccountId = customerAccountId;
        ContractNumber = contractNumber.Trim().ToUpperInvariant();
        Title = title.Trim();
        TermsText = termsText.Trim();
        DiscountPercent = Math.Round(discountPercent, 2, MidpointRounding.AwayFromZero);
        EffectiveFrom = effectiveFrom;
        ExpiresAt = expiresAt;
        IsActive = true;
    }

    internal void Deactivate() => IsActive = false;
}
