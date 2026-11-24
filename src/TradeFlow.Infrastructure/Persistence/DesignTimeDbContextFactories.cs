using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TradeFlow.Infrastructure.Modules.Catalog.Persistence;
using TradeFlow.Infrastructure.Modules.Customers.Persistence;
using TradeFlow.Infrastructure.Modules.Inventory.Persistence;
using TradeFlow.Infrastructure.Modules.Orders.Persistence;

namespace TradeFlow.Infrastructure.Persistence;

/// <summary>
/// Design-time factories used by <c>dotnet ef migrations</c> CLI and CI/CD migration bundle generation
/// so schema migrations can be scaffolded and bundled without starting the full web host or message brokers.
/// </summary>
public sealed class OrdersDesignTimeDbContextFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    public OrdersDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OrdersDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=tradeflow;Username=postgres;Password=postgres",
            npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(OrdersDbContext).Assembly.FullName);
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "orders");
            });

        return new OrdersDbContext(optionsBuilder.Options);
    }
}

public sealed class InventoryDesignTimeDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=tradeflow;Username=postgres;Password=postgres",
            npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(InventoryDbContext).Assembly.FullName);
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory");
            });

        return new InventoryDbContext(optionsBuilder.Options);
    }
}

public sealed class CatalogDesignTimeDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CatalogDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=tradeflow;Username=postgres;Password=postgres",
            npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(CatalogDbContext).Assembly.FullName);
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "catalog");
            });

        return new CatalogDbContext(optionsBuilder.Options);
    }
}

public sealed class CustomersDesignTimeDbContextFactory : IDesignTimeDbContextFactory<CustomersDbContext>
{
    public CustomersDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CustomersDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=tradeflow;Username=postgres;Password=postgres",
            npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(CustomersDbContext).Assembly.FullName);
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "customers");
            });

        return new CustomersDbContext(optionsBuilder.Options);
    }
}
