using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TradeFlow.Infrastructure.Modules.Inventory.Persistence;
using TradeFlow.Infrastructure.Modules.Orders.Persistence;
using Xunit;

namespace TradeFlow.Api.IntegrationTests.Infrastructure;

public class TradeFlowWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tradeflow_api_test")
        .WithUsername("test_user")
        .WithPassword("test_pass")
        .Build();

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        await _dbContainer.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptors = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<OrdersDbContext>) ||
                d.ServiceType == typeof(DbContextOptions<InventoryDbContext>) ||
                d.ServiceType == typeof(OrdersDbContext) ||
                d.ServiceType == typeof(InventoryDbContext)).ToList();

            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            var connectionString = _dbContainer.GetConnectionString();

            services.AddDbContext<OrdersDbContext>(options =>
                options.UseNpgsql(connectionString));

            services.AddDbContext<InventoryDbContext>(options =>
                options.UseNpgsql(connectionString));

            // Initialize PostgreSQL schemas and tables for both bounded contexts
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();

            var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            ordersDb.Database.EnsureCreated();

            var inventoryDb = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            inventoryDb.Database.ExecuteSqlRaw("CREATE SCHEMA IF NOT EXISTS inventory;");
            var inventoryCreator = (RelationalDatabaseCreator)inventoryDb.Database.GetService<IDatabaseCreator>();
            inventoryCreator.CreateTables();
        });
    }
}
