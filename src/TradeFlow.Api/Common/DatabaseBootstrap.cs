using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Inventory;
using TradeFlow.Infrastructure.Modules.Inventory.Persistence;
using TradeFlow.Infrastructure.Modules.Orders.Persistence;

namespace TradeFlow.Api.Common;

/// <summary>
/// Development helper that ensures PostgreSQL schemas (orders, inventory) and tables exist,
/// and seeds initial inventory data so orders can be placed and tested locally without manual SQL scripts.
/// </summary>
public static class DatabaseBootstrap
{
    public static readonly Guid SeedProductId1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid SeedProductId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static async Task MigrateAndSeedDevelopmentDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseBootstrap");
        var configuration = services.GetRequiredService<IConfiguration>();

        var useInMemory = bool.TryParse(configuration["UseInMemoryDatabase"], out var isInMemory) && isInMemory;
        if (useInMemory)
        {
            return;
        }

        var ordersContext = services.GetRequiredService<OrdersDbContext>();
        var inventoryContext = services.GetRequiredService<InventoryDbContext>();

        try
        {
            var canConnect = await ordersContext.Database.CanConnectAsync();
            if (!canConnect)
            {
                logger.LogWarning("Cannot connect to PostgreSQL at this time. Skipping automatic development database migration.");
                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to connect to database for development bootstrap. Skipping.");
            return;
        }

        logger.LogInformation("Ensuring PostgreSQL schemas and tables exist for development...");

        // 1. Ensure schemas exist
        await ordersContext.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS orders;");
        await ordersContext.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS inventory;");

        // 2. Ensure Orders tables exist
        var ordersTableExists = await TableExistsAsync(ordersContext, "orders", "orders");
        if (!ordersTableExists)
        {
            logger.LogInformation("Creating tables for Orders module (orders schema)...");
            var ordersCreator = (RelationalDatabaseCreator)ordersContext.Database.GetService<IDatabaseCreator>();
            await ordersCreator.CreateTablesAsync();
        }

        // 3. Ensure Inventory tables exist
        var inventoryTableExists = await TableExistsAsync(inventoryContext, "inventory", "inventory_items");
        if (!inventoryTableExists)
        {
            logger.LogInformation("Creating tables for Inventory module (inventory schema)...");
            var inventoryCreator = (RelationalDatabaseCreator)inventoryContext.Database.GetService<IDatabaseCreator>();
            await inventoryCreator.CreateTablesAsync();
        }

        // 4. Seed initial inventory items if table is empty
        var hasInventoryItems = await inventoryContext.InventoryItems.AnyAsync();
        if (!hasInventoryItems)
        {
            logger.LogInformation("Seeding initial inventory items for local testing...");

            var item1 = InventoryItem.Create(
                new ProductId(SeedProductId1),
                "SKU-STEEL-001",
                initialQuantity: 100);

            var item2 = InventoryItem.Create(
                new ProductId(SeedProductId2),
                "SKU-COPPER-002",
                initialQuantity: 50);

            await inventoryContext.InventoryItems.AddRangeAsync(item1, item2);
            await inventoryContext.SaveChangesAsync();

            logger.LogInformation(
                "Seeded SKU-STEEL-001 (100 units, ID: {Id1}) and SKU-COPPER-002 (50 units, ID: {Id2}).",
                SeedProductId1,
                SeedProductId2);
        }

        logger.LogInformation("Development database initialization complete.");
    }

    private static async Task<bool> TableExistsAsync(DbContext context, string schema, string table)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema AND table_name = @table
            );";

        var schemaParam = command.CreateParameter();
        schemaParam.ParameterName = "@schema";
        schemaParam.Value = schema;
        command.Parameters.Add(schemaParam);

        var tableParam = command.CreateParameter();
        tableParam.ParameterName = "@table";
        tableParam.Value = table;
        command.Parameters.Add(tableParam);

        var result = await command.ExecuteScalarAsync();
        return result is bool exists && exists;
    }
}
