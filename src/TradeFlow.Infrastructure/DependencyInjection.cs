using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradeFlow.Application.Inventory.Contracts;
using TradeFlow.Application.Inventory.Services;
using TradeFlow.Application.Orders.Gateways;
using TradeFlow.Domain.Inventory;
using TradeFlow.Domain.Orders;
using TradeFlow.Infrastructure.Modules.Inventory.Persistence;
using TradeFlow.Infrastructure.Modules.Orders.Gateways;
using TradeFlow.Infrastructure.Modules.Orders.Persistence;
using TradeFlow.Infrastructure.Persistence;

namespace TradeFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var useInMemory = bool.TryParse(configuration["UseInMemoryDatabase"], out var isInMemory) && isInMemory;
        var connectionString = configuration.GetConnectionString("Database")
            ?? "Host=localhost;Port=5432;Database=tradeflow;Username=postgres;Password=postgres";

        services.AddDbContext<OrdersDbContext>(options =>
        {
            if (useInMemory)
            {
                var dbName = configuration["InMemoryDatabaseName"] ?? "TradeFlow_Orders_TestDb";
                options.UseInMemoryDatabase(dbName);
            }
            else
            {
                options.UseNpgsql(connectionString, npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsAssembly(typeof(OrdersDbContext).Assembly.FullName);
                    npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
                });
            }
        });

        services.AddDbContext<InventoryDbContext>(options =>
        {
            if (useInMemory)
            {
                var dbName = configuration["InMemoryDatabaseName"] ?? "TradeFlow_Inventory_TestDb";
                options.UseInMemoryDatabase(dbName);
            }
            else
            {
                options.UseNpgsql(connectionString, npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsAssembly(typeof(InventoryDbContext).Assembly.FullName);
                    npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
                });
            }
        });

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IInventoryModule, InventoryModule>();
        services.AddScoped<IInventoryReservationService, InventoryReservationServiceAdapter>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
