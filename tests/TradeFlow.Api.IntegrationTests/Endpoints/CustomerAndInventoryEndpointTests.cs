using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TradeFlow.Api.Endpoints;
using TradeFlow.Api.IntegrationTests.Infrastructure;
using TradeFlow.Application.Common.Security;
using TradeFlow.Application.Customers.Queries;
using TradeFlow.Application.Inventory.Queries.GetInventoryItems;
using TradeFlow.Application.Orders.Queries.GetOrderById;
using TradeFlow.Application.Orders.Queries.GetOrdersSummary;
using Xunit;

namespace TradeFlow.Api.IntegrationTests.Endpoints;

public class CustomerAndInventoryEndpointTests
{
    private static WebApplicationFactory<Program> CreateInMemoryApiFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", $"TradeFlow_CustInv_{Guid.NewGuid()}");
            builder.UseSetting("ConnectionStrings:RabbitMq", string.Empty);
            builder.UseSetting("ConnectionStrings:Kafka", string.Empty);
            builder.UseSetting("ConnectionStrings:Redis", string.Empty);
            builder.UseSetting("Outbox:BackgroundWorkerEnabled", "false");
            builder.UseSetting("Authentication:Authority", TestJwtTokenGenerator.Issuer);
            builder.UseSetting("Authentication:Audience", TestJwtTokenGenerator.Audience);
            builder.UseSetting("Authentication:RequireHttpsMetadata", "false");

            builder.ConfigureServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = null;
                    options.MetadataAddress = null!;
                    options.TokenValidationParameters.ValidIssuer = TestJwtTokenGenerator.Issuer;
                    options.TokenValidationParameters.ValidAudiences = [TestJwtTokenGenerator.Audience, "account"];
                    options.TokenValidationParameters.IssuerSigningKey = TestJwtTokenGenerator.SigningKey;
                    options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                });
            });
        });
    }

    [Fact]
    public async Task CustomersEndpoints_ShouldEnforceTenantIsolationAndAdminWritePolicy()
    {
        await using var factory = CreateInMemoryApiFactory();
        using var client = factory.CreateClient();

        var alphaTenantId = Guid.NewGuid();
        var adminToken = TestJwtTokenGenerator.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), TradeFlowRoles.Admin);
        var alphaTraderToken = TestJwtTokenGenerator.GenerateToken(Guid.NewGuid(), alphaTenantId, TradeFlowRoles.Trader);

        // 1. Admin creates Alpha corporate account & confidential contract
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var createResp = await client.PostAsJsonAsync("/api/customers", new CreateCustomerAccountRequest(
            "Alpha Trading Corp", "US-94-ALPHA", "Enterprise", 500_000m, "USD", 45, alphaTenantId));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var contractResp = await client.PostAsJsonAsync($"/api/customers/{alphaTenantId}/contracts", new AddCustomerContractRequest(
            "MSA-ALPHA-01",
            "Alpha Rebate Agreement",
            "Confidential 6.5% volume discount.",
            6.5m,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1)));
        contractResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // 2. Trader Alpha accesses GET /api/customers/me -> 200 OK with own contracts
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", alphaTraderToken);
        var meResp = await client.GetAsync("/api/customers/me");
        meResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var myAccount = await meResp.Content.ReadFromJsonAsync<CustomerAccountResponse>();
        myAccount!.CompanyName.Should().Be("Alpha Trading Corp");
        myAccount.Contracts.Should().ContainSingle(c => c.ContractNumber == "MSA-ALPHA-01");

        // 3. Trader Alpha attempts GET /api/customers (list all tenants) -> 403 Forbidden
        var listAllResp = await client.GetAsync("/api/customers");
        listAllResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 4. Trader Alpha attempts POST /api/customers -> 403 Forbidden
        var forbiddenCreate = await client.PostAsJsonAsync("/api/customers", new CreateCustomerAccountRequest(
            "Evil Corp", "EVIL-1", "Standard", 1000m));
        forbiddenCreate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task InventoryRestockAndOrdersSummary_ShouldWorkEndToEnd()
    {
        await using var factory = CreateInMemoryApiFactory();
        using var client = factory.CreateClient();

        var tenantId = Guid.NewGuid();
        var adminToken = TestJwtTokenGenerator.GenerateToken(Guid.NewGuid(), tenantId, TradeFlowRoles.Admin);
        var traderToken = TestJwtTokenGenerator.GenerateToken(Guid.NewGuid(), tenantId, TradeFlowRoles.Trader);

        // 1. Trader cannot register or restock inventory
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", traderToken);
        var productId = Guid.NewGuid();
        var forbiddenInv = await client.PostAsJsonAsync("/api/inventory", new CreateInventoryItemRequest(productId, "SKU-ZINC-006", 20));
        forbiddenInv.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. Admin registers SKU and restocks +30 units -> total 50
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var createInv = await client.PostAsJsonAsync("/api/inventory", new CreateInventoryItemRequest(productId, "SKU-ZINC-006", 20));
        createInv.StatusCode.Should().Be(HttpStatusCode.Created);

        var restock = await client.PostAsJsonAsync($"/api/inventory/{productId}/restock", new RestockInventoryRequest(30));
        restock.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await restock.Content.ReadFromJsonAsync<InventoryItemResponse>();
        updated!.QuantityOnHand.Should().Be(50);
        updated.AvailableQuantity.Should().Be(50);

        // 3. Trader creates draft order, primes GET /api/orders/{id} cache (0 lines), adds a line,
        // and immediately sees the updated line without stale cache delay!
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", traderToken);
        var createOrder = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest("USD"));
        createOrder.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderId = await createOrder.Content.ReadFromJsonAsync<Guid>();

        var initialGet = await client.GetFromJsonAsync<OrderResponse>($"/api/orders/{orderId}");
        initialGet!.Lines.Should().BeEmpty();

        var addLine = await client.PostAsJsonAsync(
            $"/api/orders/{orderId}/lines",
            new AddOrderLineRequest(productId, "SKU-ZINC-006", "Zinc Ingot SHG", 2850m, 2));
        addLine.StatusCode.Should().Be(HttpStatusCode.Created);

        var afterLineGet = await client.GetFromJsonAsync<OrderResponse>($"/api/orders/{orderId}");
        afterLineGet!.Lines.Should().ContainSingle(l => l.Sku == "SKU-ZINC-006" && l.Quantity == 2);

        var summaryResp = await client.GetAsync("/api/orders/summary");
        summaryResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await summaryResp.Content.ReadFromJsonAsync<OrdersSummaryResponse>();
        summary!.TotalOrders.Should().Be(1);
        summary.DraftCount.Should().Be(1);
    }
}
