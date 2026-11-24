namespace TradeFlow.Domain.Customers;

public interface ICustomerAccountRepository
{
    Task<CustomerAccount?> GetByIdAsync(CustomerAccountId id, CancellationToken cancellationToken = default);
    Task<CustomerAccount?> GetByTaxIdAsync(string taxId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerAccount>> ListAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(CustomerAccount account, CancellationToken cancellationToken = default);
    void Update(CustomerAccount account);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
