using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
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
using TradeFlow.Infrastructure.Modules.Orders.Persistence.Outbox;
using TradeFlow.Infrastructure.Persistence;

namespace TradeFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<DomainEventsToOutboxMessagesInterceptor>();

        var useInMemory = bool.TryParse(configuration["UseInMemoryDatabase"], out var isInMemory) && isInMemory;
        var connectionString = configuration.GetConnectionString("Database")
            ?? "Host=localhost;Port=5432;Database=tradeflow;Username=postgres;Password=postgres";

        if (useInMemory)
        {
            var dbName = configuration["InMemoryDatabaseName"];
            services.AddModuleDbContext<OrdersDbContext>((sp, options) =>
                options.UseInMemoryDatabase(dbName ?? "TradeFlow_Orders_TestDb")
                       .AddInterceptors(sp.GetRequiredService<DomainEventsToOutboxMessagesInterceptor>()));
            services.AddModuleDbContext<InventoryDbContext>(options =>
                options.UseInMemoryDatabase(dbName ?? "TradeFlow_Inventory_TestDb"));
        }
        else
        {
            services.AddNpgsqlModuleDbContexts(connectionString);
        }

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IInventoryModule, InventoryModule>();
        services.AddScoped<IInventoryReservationService, InventoryReservationServiceAdapter>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }

    /// <summary>
    /// Registers every module DbContext on a single request-scoped PostgreSQL connection so the
    /// <see cref="UnitOfWork"/> can coordinate one physical transaction (ADR-0010).
    /// Adding a new module only requires one additional <c>AddModuleDbContext</c> line here.
    /// </summary>
    public static IServiceCollection AddNpgsqlModuleDbContexts(
        this IServiceCollection services,
        string connectionString,
        Action<NpgsqlDbContextOptionsBuilder>? configureNpgsql = null)
    {
        services.AddSingleton<DomainEventsToOutboxMessagesInterceptor>();
        services.AddScoped<DbConnection>(_ => new NpgsqlConnection(connectionString));

        services.AddModuleDbContext<OrdersDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<DbConnection>(), npgsql => ConfigureNpgsql(npgsql, typeof(OrdersDbContext), configureNpgsql))
                   .AddInterceptors(sp.GetRequiredService<DomainEventsToOutboxMessagesInterceptor>()));

        services.AddModuleDbContext<InventoryDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<DbConnection>(), npgsql => ConfigureNpgsql(npgsql, typeof(InventoryDbContext), configureNpgsql)));

        return services;
    }

    private static void ConfigureNpgsql(
        NpgsqlDbContextOptionsBuilder npgsql,
        Type contextType,
        Action<NpgsqlDbContextOptionsBuilder>? extra)
    {
        npgsql.MigrationsAssembly(contextType.Assembly.FullName);
        extra?.Invoke(npgsql);
    }

    private static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configure)
        where TContext : DbContext, IModuleDbContext
        => services.AddModuleDbContext<TContext>((_, options) => configure(options));

    private static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> configure)
        where TContext : DbContext, IModuleDbContext
    {
        services.AddDbContext<TContext>(configure);
        services.AddScoped<IModuleDbContext>(sp => sp.GetRequiredService<TContext>());
        return services;
    }
}
