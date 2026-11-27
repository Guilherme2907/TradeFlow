using TradeFlow.Application.Customers.Contracts;
using TradeFlow.Domain.Customers;

namespace TradeFlow.Application.Customers.Services;

public sealed class CustomersModule : ICustomersModule
{
    private readonly ICustomerAccountRepository _repository;

    public CustomersModule(ICustomerAccountRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<CustomerContractKnowledgeSnapshot>> ListActiveContractSnapshotsAsync(
        CancellationToken cancellationToken = default)
    {
        var accounts = await _repository.ListAllAsync(cancellationToken);
        var snapshots = new List<CustomerContractKnowledgeSnapshot>();

        foreach (var account in accounts.Where(a => a.IsActive))
        {
            foreach (var contract in account.Contracts.Where(c => c.IsActive))
            {
                snapshots.Add(new CustomerContractKnowledgeSnapshot(
                    account.Id.Value,
                    account.CompanyName,
                    account.TaxId,
                    account.Tier.ToString(),
                    account.CreditLimit,
                    account.Currency,
                    account.PaymentTermsDays,
                    contract.ContractNumber,
                    contract.Title,
                    contract.TermsText,
                    contract.DiscountPercent,
                    contract.EffectiveFrom,
                    contract.ExpiresAt));
            }
        }

        return snapshots;
    }
}
