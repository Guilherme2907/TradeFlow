using FluentAssertions;
using TradeFlow.Domain.Customers;
using TradeFlow.Domain.Customers.Events;
using TradeFlow.Domain.Customers.Exceptions;
using Xunit;

namespace TradeFlow.Domain.UnitTests.Customers;

public class CustomerAccountTests
{
    [Fact]
    public void Create_WithValidParameters_ShouldNormalizeTaxIdAndCurrency()
    {
        var account = CustomerAccount.Create("Alpha Corp", " us-94-111 ", CustomerTier.Enterprise, 250_000m, "usd", 45);

        account.TaxId.Should().Be("US-94-111");
        account.Currency.Should().Be("USD");
        account.CreditLimit.Should().Be(250_000m);
        account.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<CustomerAccountCreatedDomainEvent>();
    }

    [Fact]
    public void AddContract_WithDuplicateNumber_ShouldThrowCustomerInvariantException()
    {
        var account = CustomerAccount.Create("Alpha Corp", "US-94-111", CustomerTier.Enterprise, 250_000m);
        var start = DateTimeOffset.UtcNow;
        var end = start.AddYears(1);

        account.AddContract("MSA-2026", "Supply Terms", "Confidential clauses.", 5m, start, end);

        var act = () => account.AddContract("msa-2026", "Duplicate", "Clauses.", 5m, start, end);

        act.Should().Throw<CustomerInvariantException>()
            .Which.Code.Should().Be("CONTRACT_DUPLICATE_NUMBER");
    }

    [Fact]
    public void AddContract_WithExpirationBeforeEffectiveDate_ShouldThrow()
    {
        var account = CustomerAccount.Create("Beta Ltd", "EU-222", CustomerTier.Gold, 100_000m);
        var start = DateTimeOffset.UtcNow;

        var act = () => account.AddContract("MSA-BAD", "Bad Dates", "Terms", 2m, start, start.AddDays(-1));

        act.Should().Throw<CustomerInvariantException>()
            .Which.Code.Should().Be("CONTRACT_INVALID_DATES");
    }
}
