using TradeFlow.Domain.Common;

namespace TradeFlow.Domain.Customers.Exceptions;

public sealed class CustomerInvariantException : DomainException
{
    private CustomerInvariantException(string code, string message) : base(code, message)
    {
    }

    public static CustomerInvariantException InvalidCompanyName()
        => new("CUSTOMER_INVALID_COMPANY_NAME", "Company name must not be empty or exceed 200 characters.");

    public static CustomerInvariantException InvalidTaxId()
        => new("CUSTOMER_INVALID_TAX_ID", "Tax ID must not be empty or exceed 50 characters.");

    public static CustomerInvariantException InvalidCreditLimit(decimal creditLimit)
        => new("CUSTOMER_INVALID_CREDIT_LIMIT", $"Credit limit cannot be negative. Provided: {creditLimit}.");

    public static CustomerInvariantException InvalidCurrency(string currency)
        => new("CUSTOMER_INVALID_CURRENCY", $"Currency must be a valid 3-letter ISO code. Provided: '{currency}'.");

    public static CustomerInvariantException InvalidPaymentTerms(int days)
        => new("CUSTOMER_INVALID_PAYMENT_TERMS", $"Payment terms must be between 0 and 180 days. Provided: {days}.");

    public static CustomerInvariantException InvalidContractTitle()
        => new("CONTRACT_INVALID_TITLE", "Contract title must not be empty or exceed 200 characters.");

    public static CustomerInvariantException InvalidContractNumber()
        => new("CONTRACT_INVALID_NUMBER", "Contract number must not be empty or exceed 80 characters.");

    public static CustomerInvariantException InvalidContractTerms()
        => new("CONTRACT_INVALID_TERMS", "Contract terms text must not be empty or exceed 4000 characters.");

    public static CustomerInvariantException InvalidDiscount(decimal discount)
        => new("CONTRACT_INVALID_DISCOUNT", $"Negotiated discount percent must be between 0 and 50. Provided: {discount}.");

    public static CustomerInvariantException InvalidContractDateRange()
        => new("CONTRACT_INVALID_DATES", "Contract expiration date must be after effective date.");

    public static CustomerInvariantException DuplicateContractNumber(string contractNumber)
        => new("CONTRACT_DUPLICATE_NUMBER", $"Contract '{contractNumber}' already exists for this customer.");
}
