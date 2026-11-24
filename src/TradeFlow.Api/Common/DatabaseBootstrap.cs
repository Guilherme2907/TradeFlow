using Microsoft.EntityFrameworkCore;
using TradeFlow.Domain.Catalog;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Customers;
using TradeFlow.Domain.Inventory;
using TradeFlow.Infrastructure.Modules.Catalog.Persistence;
using TradeFlow.Infrastructure.Modules.Customers.Persistence;
using TradeFlow.Infrastructure.Modules.Inventory.Persistence;
using TradeFlow.Infrastructure.Modules.Orders.Persistence;

namespace TradeFlow.Api.Common;

/// <summary>
/// Development helper that applies EF Core schema migrations for each module (<c>orders</c> and <c>inventory</c>)
/// and seeds initial inventory data so orders can be placed and tested locally without manual SQL scripts.
/// Note: In Production (Kubernetes / Cloud), migrations are executed via CI/CD Migration Bundles or Init Jobs,
/// never concurrently inside <c>Program.cs</c> across scaling pods (ADR-0017).
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
        var catalogContext = services.GetRequiredService<CatalogDbContext>();
        var customersContext = services.GetRequiredService<CustomersDbContext>();

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

        logger.LogInformation("Applying PostgreSQL EF Core migrations for Orders and Inventory schemas (ADR-0017)...");

        // 1. Ensure schemas exist
        await ordersContext.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS orders;");
        await ordersContext.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS inventory;");
        await ordersContext.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS catalog;");
        await ordersContext.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS customers;");

        // 2. Baseline existing local Docker development databases created prior to Phase 9 EF Core Migrations
        await BaselineLegacySchemaIfTablesAlreadyExistAsync(
            ordersContext,
            schema: "orders",
            markerTable: "orders",
            initialMigrationId: "20261113213001_InitialOrdersSchema");

        await BaselineLegacySchemaIfTablesAlreadyExistAsync(
            inventoryContext,
            schema: "inventory",
            markerTable: "inventory_items",
            initialMigrationId: "20261113213006_InitialInventorySchema");

        // 3. Apply pending EF Core migrations per module DbContext
        await ordersContext.Database.MigrateAsync();
        await inventoryContext.Database.MigrateAsync();
        await catalogContext.Database.MigrateAsync();
        await customersContext.Database.MigrateAsync();

        // 4. Seed initial catalog products (rich industrial commodities for trading & upcoming AI/RAG)
        var seededProducts = CreateSeedCatalogProducts();
        var hasProducts = await catalogContext.Products.AnyAsync();
        if (!hasProducts)
        {
            logger.LogInformation("Seeding initial industrial commodities into Catalog schema...");
            await catalogContext.Products.AddRangeAsync(seededProducts);
            await catalogContext.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} catalog products.", seededProducts.Count);
        }

        // 5. Seed initial B2B customer accounts & confidential contracts (for RBAC & multi-tenant RAG)
        var hasCustomers = await customersContext.CustomerAccounts.AnyAsync();
        if (!hasCustomers)
        {
            logger.LogInformation("Seeding B2B customer accounts and confidential contracts into Customers schema...");
            var seededCustomers = CreateSeedCustomerAccounts();
            await customersContext.CustomerAccounts.AddRangeAsync(seededCustomers);
            await customersContext.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} B2B customer accounts.", seededCustomers.Count);
        }

        // 6. Seed initial inventory items for all catalog commodities if table is empty
        var hasInventoryItems = await inventoryContext.InventoryItems.AnyAsync();
        if (!hasInventoryItems)
        {
            logger.LogInformation("Seeding initial inventory items for local testing...");

            var inventoryItems = seededProducts.Select((p, idx) =>
                InventoryItem.Create(
                    p.Id,
                    p.Sku,
                    initialQuantity: idx switch
                    {
                        0 => 100,
                        1 => 50,
                        _ => 40 + (idx * 10)
                    })).ToList();

            await inventoryContext.InventoryItems.AddRangeAsync(inventoryItems);
            await inventoryContext.SaveChangesAsync();

            logger.LogInformation("Seeded {Count} inventory SKUs.", inventoryItems.Count);
        }

        logger.LogInformation("Development database migration and seed complete.");
    }

    private static async Task BaselineLegacySchemaIfTablesAlreadyExistAsync(
        DbContext context,
        string schema,
        string markerTable,
        string initialMigrationId)
    {
        var tableExists = await TableExistsAsync(context, schema, markerTable);
        var historyExists = await TableExistsAsync(context, schema, "__EFMigrationsHistory");

        if (tableExists && !historyExists)
        {
            var createHistorySql = $"""
                CREATE TABLE IF NOT EXISTS {schema}."__EFMigrationsHistory" (
                    "MigrationId" character varying(150) NOT NULL,
                    "ProductVersion" character varying(32) NOT NULL,
                    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
                );
                INSERT INTO {schema}."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('{initialMigrationId}', '10.0.4')
                ON CONFLICT DO NOTHING;
                """;
            await context.Database.ExecuteSqlRawAsync(createHistorySql);
        }
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

    private static IReadOnlyList<Product> CreateSeedCatalogProducts() =>
    [
        Product.Create(
            "SKU-STEEL-001",
            "Cold-Rolled Industrial Steel Coil",
            "Ferrous Metals",
            "ASTM A1008 commercial-grade cold-rolled carbon steel coil, thickness 1.2mm, width 1250mm, oiled surface finish for automotive stamping and structural panels.",
            1250.00m,
            "USD",
            "Coil",
            minimumOrderQuantity: 1,
            explicitId: new ProductId(SeedProductId1)),
        Product.Create(
            "SKU-COPPER-002",
            "Electrolytic Copper Cathode Grade A",
            "Non-Ferrous Metals",
            "LME Grade A electrolytic copper cathode, 99.99% Cu purity (ASTM B115), high electrical conductivity for industrial power cables, busbars, and transformer windings.",
            890.00m,
            "USD",
            "Metric Ton",
            minimumOrderQuantity: 1,
            explicitId: new ProductId(SeedProductId2)),
        Product.Create(
            "SKU-ALUM-003",
            "Aerospace Aluminum Sheet 7075-T6",
            "Non-Ferrous Metals",
            "High-strength zinc-alloyed aluminum plate 7075-T6 (AMS 4045), tensile strength 572 MPa, corrosion-resistant lightweight alloy for aerospace frames and precision tooling.",
            3450.00m,
            "USD",
            "Metric Ton",
            minimumOrderQuantity: 1,
            explicitId: new ProductId(Guid.Parse("33333333-3333-3333-3333-333333333333"))),
        Product.Create(
            "SKU-STAINLESS-004",
            "Stainless Steel 316L Marine Plate",
            "Ferrous Metals",
            "Austenitic molybdenum-alloyed stainless steel 316L plate (ASTM A240), superior chloride pitting resistance for chemical reactors, offshore rigs, and food-grade tanks.",
            4120.00m,
            "USD",
            "Metric Ton",
            minimumOrderQuantity: 1,
            explicitId: new ProductId(Guid.Parse("44444444-4444-4444-4444-444444444444"))),
        Product.Create(
            "SKU-NICKEL-005",
            "Refined Nickel Briquettes Class 1",
            "Specialty Alloys",
            "99.8% purity Class 1 nickel briquettes suitable for stainless steel melting, battery precursor sulfate production, and high-temperature superalloys.",
            16800.00m,
            "USD",
            "Metric Ton",
            minimumOrderQuantity: 1,
            explicitId: new ProductId(Guid.Parse("55555555-5555-5555-5555-555555555555"))),
        Product.Create(
            "SKU-ZINC-006",
            "Special High-Grade Zinc Ingot (SHG)",
            "Non-Ferrous Metals",
            "99.995% pure SHG zinc ingots (ASTM B6) engineered for continuous hot-dip galvanizing lines, die-casting alloys, and brass manufacturing.",
            2780.00m,
            "USD",
            "Metric Ton",
            minimumOrderQuantity: 2,
            explicitId: new ProductId(Guid.Parse("66666666-6666-6666-6666-666666666666"))),
        Product.Create(
            "SKU-HDPE-007",
            "High-Density Polyethylene Resin (HDPE) Blow Molding",
            "Industrial Polymers",
            "High molecular weight HDPE copolymer pellets (density 0.954 g/cm3, MFI 0.35 g/10min) with high environmental stress crack resistance for industrial chemical drums.",
            1420.00m,
            "USD",
            "Metric Ton",
            minimumOrderQuantity: 5,
            explicitId: new ProductId(Guid.Parse("77777777-7777-7777-7777-777777777777"))),
        Product.Create(
            "SKU-TITAN-008",
            "Titanium Bar Grade 5 (Ti-6Al-4V)",
            "Specialty Alloys",
            "Annealed Grade 5 titanium round bar (ASTM B348), exceptional strength-to-weight ratio and thermal stability up to 400C for turbine shafts and medical/defense machining.",
            28500.00m,
            "USD",
            "Metric Ton",
            minimumOrderQuantity: 1,
            explicitId: new ProductId(Guid.Parse("88888888-8888-8888-8888-888888888888"))),
        Product.Create(
            "SKU-LITHIUM-009",
            "Battery-Grade Lithium Carbonate (Li2CO3)",
            "Energy Materials",
            "99.5% minimum purity micronized battery-grade lithium carbonate powder with low magnetic iron impurities (<10 ppm) for LFP and NMC cathode manufacturing.",
            14900.00m,
            "USD",
            "Metric Ton",
            minimumOrderQuantity: 1,
            explicitId: new ProductId(Guid.Parse("99999999-9999-9999-9999-999999999991"))),
        Product.Create(
            "SKU-REBAR-010",
            "Deformed Structural Steel Rebar Grade 60",
            "Ferrous Metals",
            "ASTM A615 Grade 60 deformed carbon steel reinforcing bars (16mm diameter, 12m length), minimum yield strength 420 MPa for heavy civil infrastructure and foundations.",
            790.00m,
            "EUR",
            "Metric Ton",
            minimumOrderQuantity: 10,
            explicitId: new ProductId(Guid.Parse("99999999-9999-9999-9999-999999999992")))
    ];

    private static IReadOnlyList<CustomerAccount> CreateSeedCustomerAccounts()
    {
        var effectiveFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var expiresAt = new DateTimeOffset(2027, 12, 31, 23, 59, 59, TimeSpan.Zero);

        var alpha = CustomerAccount.Create(
            "Alpha Trading Corp",
            "US-94-1111001",
            CustomerTier.Enterprise,
            creditLimit: 500_000m,
            currency: "USD",
            paymentTermsDays: 45,
            explicitId: new CustomerAccountId(Guid.Parse("11111111-1111-1111-1111-111111111111")));

        alpha.AddContract(
            "MSA-ALPHA-2026",
            "Alpha Enterprise Metals Supply & Volume Rebate Agreement",
            "CONFIDENTIAL (Alpha Trading Corp): Eligible for 6.5% volume discount on Ferrous Metals orders exceeding 25 metric tons. Free order cancellation within 24 hours of submission; 3% restocking fee after 24 hours. Maximum single-order credit exposure USD 250,000 without RiskManager pre-approval. Delivery SLA: 5 business days FOB Houston.",
            discountPercent: 6.5m,
            effectiveFrom,
            expiresAt);

        var beta = CustomerAccount.Create(
            "Beta Industries Ltd",
            "EU-88-2222002",
            CustomerTier.Gold,
            creditLimit: 180_000m,
            currency: "USD",
            paymentTermsDays: 30,
            explicitId: new CustomerAccountId(Guid.Parse("22222222-2222-2222-2222-222222222222")));

        beta.AddContract(
            "MSA-BETA-2026",
            "Beta Specialty Alloys & Copper Framework Agreement",
            "CONFIDENTIAL (Beta Industries Ltd): Fixed 4.0% negotiated discount on Electrolytic Copper (SKU-COPPER-002) and Aerospace Aluminum (SKU-ALUM-003). Cancellations after submission incur a flat 8% penalty fee with zero grace window. Orders above USD 90,000 require secondary CFO sign-off. Delivery SLA: 7 business days CIF Rotterdam.",
            discountPercent: 4.0m,
            effectiveFrom,
            expiresAt);

        var admin = CustomerAccount.Create(
            "TradeFlow Internal Treasury Desk",
            "TF-00-9999009",
            CustomerTier.Enterprise,
            creditLimit: 2_000_000m,
            currency: "USD",
            paymentTermsDays: 60,
            explicitId: new CustomerAccountId(Guid.Parse("99999999-9999-9999-9999-999999999999")));

        admin.AddContract(
            "INT-TREASURY-2026",
            "Internal Hedging & Liquidity Operations Policy",
            "Internal platform desk authorized for cross-tenant liquidity balancing, emergency restocking, and manual risk intervention across all commodity categories.",
            discountPercent: 0m,
            effectiveFrom,
            expiresAt);

        return [alpha, beta, admin];
    }
}
