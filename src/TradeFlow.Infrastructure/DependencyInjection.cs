using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradeFlow.Application.Catalog.Contracts;
using TradeFlow.Application.Catalog.Services;
using TradeFlow.Application.Common.Caching;
using TradeFlow.Application.Customers.Contracts;
using TradeFlow.Application.Customers.Services;
using TradeFlow.Application.Inventory.Contracts;
using TradeFlow.Application.Inventory.Services;
using TradeFlow.Application.Orders.Gateways;
using TradeFlow.Domain.Catalog;
using TradeFlow.Domain.Customers;
using TradeFlow.Domain.Inventory;
using TradeFlow.Domain.Orders;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradeFlow.Infrastructure.Caching;
using TradeFlow.Infrastructure.Messaging;
using TradeFlow.Infrastructure.Messaging.Kafka;
using TradeFlow.Infrastructure.Messaging.RabbitMq;
using TradeFlow.Infrastructure.Modules.AI;
using TradeFlow.Infrastructure.Modules.Catalog.Persistence;
using TradeFlow.Infrastructure.Modules.Customers.Persistence;
using TradeFlow.Infrastructure.Modules.Inventory.Persistence;
using TradeFlow.Infrastructure.Modules.Orders.Consumers;
using TradeFlow.Infrastructure.Modules.Orders.Gateways;
using TradeFlow.Infrastructure.Modules.Orders.Persistence;
using TradeFlow.Infrastructure.Modules.Orders.Persistence.Inbox;
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
            services.AddModuleDbContext<CatalogDbContext>(options =>
                options.UseInMemoryDatabase(dbName ?? "TradeFlow_Catalog_TestDb"));
            services.AddModuleDbContext<CustomersDbContext>(options =>
                options.UseInMemoryDatabase(dbName ?? "TradeFlow_Customers_TestDb"));
        }
        else
        {
            services.AddNpgsqlModuleDbContexts(connectionString);
        }

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICustomerAccountRepository, CustomerAccountRepository>();
        services.AddScoped<IInventoryModule, InventoryModule>();
        services.AddScoped<ICatalogModule, CatalogModule>();
        services.AddScoped<ICustomersModule, CustomersModule>();
        services.AddScoped<IInventoryReservationService, InventoryReservationServiceAdapter>();
        services.AddScoped<ICatalogGateway, CatalogGatewayAdapter>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        var rabbitMqConnectionString = configuration.GetConnectionString("RabbitMq");
        var hasRabbitMq = !string.IsNullOrWhiteSpace(rabbitMqConnectionString);
        if (hasRabbitMq)
        {
            services.AddRabbitMqMessaging(rabbitMqConnectionString!);
        }

        var kafkaBootstrapServers = configuration.GetConnectionString("Kafka");
        var hasKafka = !string.IsNullOrWhiteSpace(kafkaBootstrapServers);
        if (hasKafka)
        {
            services.AddKafkaStreaming(kafkaBootstrapServers!);
        }

        var redisConnectionString = configuration.GetConnectionString("Redis");
        var hasRedis = !string.IsNullOrWhiteSpace(redisConnectionString);
        if (hasRedis)
        {
            services.AddRedisCaching(redisConnectionString!);
        }
        else
        {
            services.AddInMemoryCaching();
        }

        if (hasRabbitMq && hasKafka)
        {
            services.AddScoped<IOutboxMessagePublisher>(sp =>
                new RoutedOutboxMessagePublisher(
                [
                    sp.GetRequiredService<RabbitMqOutboxMessagePublisher>(),
                    sp.GetRequiredService<KafkaOutboxMessagePublisher>()
                ]));
        }
        else if (hasKafka)
        {
            services.AddScoped<IOutboxMessagePublisher>(sp => sp.GetRequiredService<KafkaOutboxMessagePublisher>());
        }
        else if (!hasRabbitMq)
        {
            services.AddScoped<IOutboxMessagePublisher, LoggingOutboxMessagePublisher>();
        }

        services.AddScoped<OutboxProcessor>();
        services.AddScoped<IdempotentInboxConsumer>();

        var workerEnabled = !bool.TryParse(configuration["Outbox:BackgroundWorkerEnabled"], out var isEnabled) || isEnabled;
        if (!useInMemory && workerEnabled)
        {
            services.AddHostedService<OutboxPublisherBackgroundService>();
        }

        services.AddAiServices(configuration);

        return services;
    }

    public static IServiceCollection AddRabbitMqMessaging(
        this IServiceCollection services,
        string rabbitMqConnectionString,
        bool enableConsumerBackgroundWorker = true)
    {
        services.AddSingleton<IRabbitMqConnectionManager>(_ => new RabbitMqConnectionManager(rabbitMqConnectionString));
        services.AddScoped<RabbitMqOutboxMessagePublisher>();
        services.AddScoped<IOutboxMessagePublisher>(sp => sp.GetRequiredService<RabbitMqOutboxMessagePublisher>());

        services.AddScoped<IIntegrationEventConsumer, OrderSubmittedInvoiceConsumer>();
        services.AddScoped<IIntegrationEventConsumer, OrderCancelledNotificationConsumer>();
        services.AddScoped<IIntegrationEventConsumer, OrderLifecycleAuditConsumer>();

        if (enableConsumerBackgroundWorker)
        {
            services.AddHostedService<RabbitMqConsumerBackgroundService>();
        }

        return services;
    }

    public static IServiceCollection AddKafkaStreaming(
        this IServiceCollection services,
        string kafkaBootstrapServers,
        bool enableConsumerBackgroundWorker = true,
        string consumerGroupId = KafkaTopology.DefaultConsumerGroup)
    {
        services.AddSingleton<MarketTradeAnalyticsHandler>();
        services.AddScoped<IMarketTradeStreamHandler>(sp => sp.GetRequiredService<MarketTradeAnalyticsHandler>());

        services.AddSingleton<IKafkaTradeStreamProducer>(sp =>
            new KafkaTradeStreamProducer(
                kafkaBootstrapServers,
                sp.GetRequiredService<ILogger<KafkaTradeStreamProducer>>()));

        services.AddScoped<KafkaOutboxMessagePublisher>();

        if (enableConsumerBackgroundWorker)
        {
            services.AddHostedService(sp =>
                new KafkaTradeStreamConsumerBackgroundService(
                    kafkaBootstrapServers,
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetRequiredService<IKafkaTradeStreamProducer>(),
                    sp.GetRequiredService<ILogger<KafkaTradeStreamConsumerBackgroundService>>(),
                    consumerGroupId));
        }

        return services;
    }

    public static IServiceCollection AddRedisCaching(
        this IServiceCollection services,
        string redisConnectionString)
    {
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(redisConnectionString);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });

        services.AddSingleton<ICacheService, RedisCacheService>();
        services.AddSingleton<IDistributedRateLimiter, RedisDistributedRateLimiter>();
        return services;
    }

    public static IServiceCollection AddInMemoryCaching(this IServiceCollection services)
    {
        services.AddSingleton<ICacheService, InMemoryCacheService>();
        services.AddSingleton<IDistributedRateLimiter, InMemoryDistributedRateLimiter>();
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
            options.UseNpgsql(sp.GetRequiredService<DbConnection>(), npgsql => ConfigureNpgsql(npgsql, typeof(OrdersDbContext), "orders", configureNpgsql))
                   .AddInterceptors(sp.GetRequiredService<DomainEventsToOutboxMessagesInterceptor>()));

        services.AddModuleDbContext<InventoryDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<DbConnection>(), npgsql => ConfigureNpgsql(npgsql, typeof(InventoryDbContext), "inventory", configureNpgsql)));

        services.AddModuleDbContext<CatalogDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<DbConnection>(), npgsql => ConfigureNpgsql(npgsql, typeof(CatalogDbContext), "catalog", configureNpgsql)));

        services.AddModuleDbContext<CustomersDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<DbConnection>(), npgsql => ConfigureNpgsql(npgsql, typeof(CustomersDbContext), "customers", configureNpgsql)));

        return services;
    }

    private static void ConfigureNpgsql(
        NpgsqlDbContextOptionsBuilder npgsql,
        Type contextType,
        string schema,
        Action<NpgsqlDbContextOptionsBuilder>? extra)
    {
        npgsql.MigrationsAssembly(contextType.Assembly.FullName);
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema);
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
