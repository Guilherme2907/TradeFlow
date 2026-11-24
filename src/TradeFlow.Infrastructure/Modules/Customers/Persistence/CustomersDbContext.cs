using Microsoft.EntityFrameworkCore;
using TradeFlow.Domain.Customers;
using TradeFlow.Infrastructure.Modules.Customers.Persistence.Configurations;
using TradeFlow.Infrastructure.Persistence;

namespace TradeFlow.Infrastructure.Modules.Customers.Persistence;

/// <summary>
/// Dedicated DbContext for the Customers bounded context (ADR-0007).
/// Fully encapsulates the 'customers' PostgreSQL schema (CustomerAccount & CustomerContract).
/// </summary>
public class CustomersDbContext : DbContext, IModuleDbContext
{
    public DbSet<CustomerAccount> CustomerAccounts => Set<CustomerAccount>();
    public DbSet<CustomerContract> CustomerContracts => Set<CustomerContract>();

    public CustomersDbContext(DbContextOptions<CustomersDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new CustomerAccountConfiguration());
        modelBuilder.ApplyConfiguration(new CustomerContractConfiguration());

        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            modelBuilder.Entity<CustomerAccount>()
                .Property(x => x.Version)
                .HasDefaultValue(0u);
        }
    }
}
