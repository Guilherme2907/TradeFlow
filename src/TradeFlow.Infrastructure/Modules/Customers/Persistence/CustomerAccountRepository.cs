using Microsoft.EntityFrameworkCore;
using TradeFlow.Domain.Customers;

namespace TradeFlow.Infrastructure.Modules.Customers.Persistence;

public sealed class CustomerAccountRepository : ICustomerAccountRepository
{
    private readonly CustomersDbContext _context;

    public CustomerAccountRepository(CustomersDbContext context)
    {
        _context = context;
    }

    public async Task<CustomerAccount?> GetByIdAsync(CustomerAccountId id, CancellationToken cancellationToken = default)
    {
        return await _context.CustomerAccounts
            .Include(x => x.Contracts)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<CustomerAccount?> GetByTaxIdAsync(string taxId, CancellationToken cancellationToken = default)
    {
        var normalized = taxId.Trim().ToUpperInvariant();
        return await _context.CustomerAccounts
            .Include(x => x.Contracts)
            .FirstOrDefaultAsync(x => x.TaxId == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerAccount>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.CustomerAccounts
            .AsNoTracking()
            .Include(x => x.Contracts)
            .OrderBy(x => x.CompanyName)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(CustomerAccount account, CancellationToken cancellationToken = default)
    {
        await _context.CustomerAccounts.AddAsync(account, cancellationToken);
    }

    public void Update(CustomerAccount account)
    {
        _context.CustomerAccounts.Update(account);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}
