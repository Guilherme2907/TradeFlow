using System.Data.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TradeFlow.Infrastructure;
using TradeFlow.Infrastructure.Modules.Inventory.Persistence;
using TradeFlow.Infrastructure.Modules.Orders.Persistence;
using TradeFlow.Infrastructure.Persistence;
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
        builder.UseSetting("ConnectionStrings:RabbitMq", string.Empty);
        builder.UseSetting("ConnectionStrings:Kafka", string.Empty);
        builder.UseSetting("Outbox:BackgroundWorkerEnabled", "false");
        builder.UseSetting("Authentication:Authority", TestJwtTokenGenerator.Issuer);
        builder.UseSetting("Authentication:Audience", TestJwtTokenGenerator.Audience);
        builder.UseSetting("Authentication:RequireHttpsMetadata", "false");

        builder.ConfigureServices(services =>
        {
            // Configure JwtBearer to validate RS256 signatures against TestJwtTokenGenerator.SigningKey without external HTTP OIDC discovery
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.MetadataAddress = null!;
                options.TokenValidationParameters.ValidIssuer = TestJwtTokenGenerator.Issuer;
                options.TokenValidationParameters.ValidAudiences = [TestJwtTokenGenerator.Audience, "account"];
                options.TokenValidationParameters.IssuerSigningKey = TestJwtTokenGenerator.SigningKey;
                options.TokenValidationParameters.ValidateIssuerSigningKey = true;
            });

            var descriptors = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<OrdersDbContext>) ||
                d.ServiceType == typeof(DbContextOptions<InventoryDbContext>) ||
                d.ServiceType == typeof(OrdersDbContext) ||
                d.ServiceType == typeof(InventoryDbContext) ||
                d.ServiceType == typeof(IModuleDbContext) ||
                d.ServiceType == typeof(DbConnection)).ToList();

            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            // Same registration path as production: module contexts sharing one scoped connection (ADR-0010)
            services.AddNpgsqlModuleDbContexts(_dbContainer.GetConnectionString());

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
