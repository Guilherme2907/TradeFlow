using Microsoft.EntityFrameworkCore;
using TradeFlow.Domain.Catalog;
using TradeFlow.Infrastructure.Modules.Catalog.Persistence.Configurations;
using TradeFlow.Infrastructure.Persistence;

namespace TradeFlow.Infrastructure.Modules.Catalog.Persistence;

/// <summary>
/// Dedicated DbContext for the Catalog bounded context (ADR-0007).
/// Fully encapsulates the 'catalog' PostgreSQL schema and product catalog entities.
/// </summary>
public class CatalogDbContext : DbContext, IModuleDbContext
{
    public DbSet<Product> Products => Set<Product>();

    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new ProductConfiguration());

        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            modelBuilder.Entity<Product>()
                .Property(x => x.Version)
                .HasDefaultValue(0u);
        }
    }
}
