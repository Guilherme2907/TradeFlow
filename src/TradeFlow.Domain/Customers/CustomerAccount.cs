using TradeFlow.Domain.Common;
using TradeFlow.Domain.Customers.Events;
using TradeFlow.Domain.Customers.Exceptions;

namespace TradeFlow.Domain.Customers;

/// <summary>
/// Aggregate root representing a B2B corporate tenant account in the Customers bounded context.
/// Owns credit limits, commercial tier, payment terms, and confidential customer contracts.
/// </summary>
public class CustomerAccount : AggregateRoot<CustomerAccountId>
{
    private readonly List<CustomerContract> _contracts = [];

    public string CompanyName { get; private set; } = string.Empty;
    public string TaxId { get; private set; } = string.Empty;
    public CustomerTier Tier { get; private set; }
    public decimal CreditLimit { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public int PaymentTermsDays { get; private set; }
    public bool IsActive { get; private set; }
    public IReadOnlyCollection<CustomerContract> Contracts => _contracts.AsReadOnly();

    private CustomerAccount() { }

    private CustomerAccount(
        CustomerAccountId id,
        string companyName,
        string taxId,
        CustomerTier tier,
        decimal creditLimit,
        string currency,
        int paymentTermsDays,
        DateTimeOffset? createdAt = null) : base(id, createdAt)
    {
        CompanyName = ValidateCompanyName(companyName);
        TaxId = ValidateTaxId(taxId);
        Tier = tier;
        CreditLimit = ValidateCreditLimit(creditLimit);
        Currency = ValidateCurrency(currency);
        PaymentTermsDays = ValidatePaymentTerms(paymentTermsDays);
        IsActive = true;

        RaiseDomainEvent(new CustomerAccountCreatedDomainEvent(Id, CompanyName, Tier, CreditLimit, Currency, CreatedAt));
    }

    public static CustomerAccount Create(
        string companyName,
        string taxId,
        CustomerTier tier,
        decimal creditLimit,
        string currency = "USD",
        int paymentTermsDays = 30,
        CustomerAccountId? explicitId = null,
        DateTimeOffset? createdAt = null)
    {
        var id = explicitId is { Value: var v } && v != Guid.Empty
            ? explicitId.Value
            : CustomerAccountId.New();

        return new CustomerAccount(id, companyName, taxId, tier, creditLimit, currency, paymentTermsDays, createdAt);
    }

    public void UpdateProfile(
        string companyName,
        CustomerTier tier,
        decimal creditLimit,
        string currency,
        int paymentTermsDays,
        bool isActive)
    {
        CompanyName = ValidateCompanyName(companyName);
        Tier = tier;
        CreditLimit = ValidateCreditLimit(creditLimit);
        Currency = ValidateCurrency(currency);
        PaymentTermsDays = ValidatePaymentTerms(paymentTermsDays);
        IsActive = isActive;
        Touch();
    }

    public CustomerContract AddContract(
        string contractNumber,
        string title,
        string termsText,
        decimal discountPercent,
        DateTimeOffset effectiveFrom,
        DateTimeOffset expiresAt)
    {
        var normalized = contractNumber.Trim().ToUpperInvariant();
        if (_contracts.Any(c => c.ContractNumber == normalized))
        {
            throw CustomerInvariantException.DuplicateContractNumber(normalized);
        }

        var contract = new CustomerContract(
            CustomerContractId.New(),
            Id,
            contractNumber,
            title,
            termsText,
            discountPercent,
            effectiveFrom,
            expiresAt);

        _contracts.Add(contract);
        Touch();

        RaiseDomainEvent(new CustomerContractAddedDomainEvent(
            Id,
            contract.Id,
            contract.ContractNumber,
            contract.Title,
            DateTimeOffset.UtcNow));

        return contract;
    }

    private static string ValidateCompanyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            throw CustomerInvariantException.InvalidCompanyName();
        return name.Trim();
    }

    private static string ValidateTaxId(string taxId)
    {
        if (string.IsNullOrWhiteSpace(taxId) || taxId.Trim().Length > 50)
            throw CustomerInvariantException.InvalidTaxId();
        return taxId.Trim().ToUpperInvariant();
    }

    private static decimal ValidateCreditLimit(decimal limit)
    {
        if (limit < 0m)
            throw CustomerInvariantException.InvalidCreditLimit(limit);
        return Math.Round(limit, 2, MidpointRounding.AwayFromZero);
    }

    private static string ValidateCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3 || !currency.Trim().All(char.IsLetter))
            throw CustomerInvariantException.InvalidCurrency(currency ?? string.Empty);
        return currency.Trim().ToUpperInvariant();
    }

    private static int ValidatePaymentTerms(int days)
    {
        if (days < 0 || days > 180)
            throw CustomerInvariantException.InvalidPaymentTerms(days);
        return days;
    }
}
