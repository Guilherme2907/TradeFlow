using Microsoft.EntityFrameworkCore;
using TradeFlow.Domain.Orders;
using TradeFlow.Infrastructure.Modules.Orders.Persistence.Configurations;

namespace TradeFlow.Infrastructure.Modules.Orders.Persistence;

/// <summary>
/// Dedicated DbContext for the Orders bounded context (ADR-0007).
/// Fully encapsulates the 'orders' PostgreSQL schema and entities (Order, OrderLine).
/// </summary>
public class OrdersDbContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    public OrdersDbContext(DbContextOptions<OrdersDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new OrderLineConfiguration());

        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            modelBuilder.Entity<Order>()
                .Property(x => x.Version)
                .HasDefaultValue(0u);
        }
    }
}
