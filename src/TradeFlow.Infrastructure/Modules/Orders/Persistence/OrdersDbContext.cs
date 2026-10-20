using Microsoft.EntityFrameworkCore;
using TradeFlow.Domain.Orders;
using TradeFlow.Infrastructure.Modules.Orders.Persistence.Configurations;
using TradeFlow.Infrastructure.Modules.Orders.Persistence.Inbox;
using TradeFlow.Infrastructure.Modules.Orders.Persistence.Outbox;
using TradeFlow.Infrastructure.Persistence;

namespace TradeFlow.Infrastructure.Modules.Orders.Persistence;

/// <summary>
/// Dedicated DbContext for the Orders bounded context (ADR-0007).
/// Fully encapsulates the 'orders' PostgreSQL schema and entities (Order, OrderLine, OutboxMessage, InboxMessage).
/// </summary>
public class OrdersDbContext : DbContext, IModuleDbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public OrdersDbContext(DbContextOptions<OrdersDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new OrderLineConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());

        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite" || Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            modelBuilder.Entity<Order>()
                .Property(x => x.Version)
                .HasDefaultValue(0u);

            modelBuilder.Entity<OutboxMessage>()
                .Property(x => x.Payload)
                .HasColumnType("TEXT");
        }
    }
}
