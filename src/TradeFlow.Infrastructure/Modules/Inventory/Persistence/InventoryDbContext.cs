using Microsoft.EntityFrameworkCore;
using TradeFlow.Domain.Inventory;
using TradeFlow.Infrastructure.Modules.Inventory.Persistence.Configurations;
using TradeFlow.Infrastructure.Persistence;

namespace TradeFlow.Infrastructure.Modules.Inventory.Persistence;

/// <summary>
/// Dedicated DbContext for the Inventory bounded context (ADR-0007).
/// Fully encapsulates the 'inventory' PostgreSQL schema and entities (InventoryItem).
/// </summary>
public class InventoryDbContext : DbContext, IModuleDbContext
{
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new InventoryItemConfiguration());

        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            modelBuilder.Entity<InventoryItem>()
                .Property(x => x.Version)
                .HasDefaultValue(0u);
        }
    }
}
