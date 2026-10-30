using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TradeFlow.Api.Endpoints;
using TradeFlow.Api.IntegrationTests.Infrastructure;
using TradeFlow.Application.Common.Security;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Inventory;
using TradeFlow.Infrastructure.Modules.Inventory.Persistence;
using Xunit;

namespace TradeFlow.Api.IntegrationTests.Endpoints;

[Trait("Category", "RequiresDocker")]
public class OrderEndpointTests : IClassFixture<TradeFlowWebApplicationFactory>
{
    private readonly TradeFlowWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public OrderEndpointTests(TradeFlowWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateAuthenticatedClient(Guid? customerId, params string[] roles)
    {
        var client = _factory.CreateClient();
        var token = TestJwtTokenGenerator.GenerateToken(Guid.NewGuid(), customerId, roles);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task CreateOrder_WithoutBearerToken_ShouldReturn401Unauthorized()
    {
        // Arrange: Anonymous client with no Authorization header
        using var anonymousClient = _factory.CreateClient();
        var request = new CreateOrderRequest(Currency: "USD", MinimumOrderValue: 200m);

        // Act
        var response = await anonymousClient.PostAsJsonAsync("/api/orders", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateOrder_WithComplianceAuditorRole_ShouldReturn403Forbidden()
    {
        // Arrange: ComplianceAuditor has read-only privileges and does not satisfy OrderWritePolicy
        using var auditorClient = CreateAuthenticatedClient(Guid.NewGuid(), TradeFlowRoles.ComplianceAuditor);
        var request = new CreateOrderRequest(Currency: "USD", MinimumOrderValue: 200m);

        // Act
        var response = await auditorClient.PostAsJsonAsync("/api/orders", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateOrder_WithValidTraderToken_ShouldReturnCreatedWithLocationAndId()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        using var traderClient = CreateAuthenticatedClient(customerId, TradeFlowRoles.Trader);
        var request = new CreateOrderRequest(Currency: "USD", MinimumOrderValue: 200m);

        // Act
        var response = await traderClient.PostAsJsonAsync("/api/orders", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().StartWith("/api/orders/");

        var orderId = await response.Content.ReadFromJsonAsync<Guid>(_jsonOptions);
        orderId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateOrder_WithInvalidCurrency_ShouldReturnBadRequestProblemDetails()
    {
        // Arrange: Invalid 4-letter currency violates FluentValidation rule
        using var traderClient = CreateAuthenticatedClient(Guid.NewGuid(), TradeFlowRoles.Trader);
        var request = new CreateOrderRequest(Currency: "USDD", MinimumOrderValue: 100m);

        // Act
        var response = await traderClient.PostAsJsonAsync("/api/orders", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problemDetails = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        problemDetails.GetProperty("status").GetInt32().Should().Be(400);
        problemDetails.GetProperty("title").GetString().Should().Be("Bad Request");
        problemDetails.GetProperty("detail").GetString().Should().Contain("Currency");
    }

    [Fact]
    public async Task AddOrderLine_WhenOrderDoesNotExist_ShouldReturnNotFoundProblemDetails()
    {
        // Arrange
        using var traderClient = CreateAuthenticatedClient(Guid.NewGuid(), TradeFlowRoles.Trader);
        var nonExistentOrderId = Guid.NewGuid();
        var request = new AddOrderLineRequest(
            ProductId: Guid.NewGuid(),
            Sku: "SKU-VALVE-01",
            ProductName: "Control Valve",
            UnitPrice: 150m,
            Quantity: 2);

        // Act
        var response = await traderClient.PostAsJsonAsync($"/api/orders/{nonExistentOrderId}/lines", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problemDetails = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        problemDetails.GetProperty("status").GetInt32().Should().Be(404);
        problemDetails.GetProperty("title").GetString().Should().Be("Not Found");
        problemDetails.GetProperty("code").GetString().Should().Be("ORDER_NOT_FOUND");
    }

    [Fact]
    public async Task AddOrderLine_WhenTraderAttemptsToModifyAnotherCustomerOrder_ShouldReturn403Forbidden_PreventingBolaIdor()
    {
        // Arrange: Trader Alpha creates an order under Tenant A
        var tenantAlpha = Guid.NewGuid();
        var tenantBeta = Guid.NewGuid();

        using var traderAlphaClient = CreateAuthenticatedClient(tenantAlpha, TradeFlowRoles.Trader);
        using var traderBetaClient = CreateAuthenticatedClient(tenantBeta, TradeFlowRoles.Trader);

        var createResponse = await traderAlphaClient.PostAsJsonAsync("/api/orders", new CreateOrderRequest(Currency: "USD"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var alphaOrderId = await createResponse.Content.ReadFromJsonAsync<Guid>(_jsonOptions);

        // Act: Trader Beta attempts to inject a line item into Trader Alpha's order (BOLA / IDOR attack)
        var attackResponse = await traderBetaClient.PostAsJsonAsync($"/api/orders/{alphaOrderId}/lines", new AddOrderLineRequest(
            ProductId: Guid.NewGuid(),
            Sku: "SKU-SABOTAGE-99",
            ProductName: "Unauthorized Item",
            UnitPrice: 9999m,
            Quantity: 10));

        // Assert: Rejected with 403 Forbidden ProblemDetails
        attackResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problemDetails = await attackResponse.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        problemDetails.GetProperty("status").GetInt32().Should().Be(403);
        problemDetails.GetProperty("code").GetString().Should().Be("ORDER_TENANT_FORBIDDEN");
    }

    [Fact]
    public async Task CancelOrder_WhenRiskManagerCancelsOrderAcrossTenants_ShouldSucceed()
    {
        // Arrange: Trader Alpha creates an order
        var tenantAlpha = Guid.NewGuid();
        using var traderAlphaClient = CreateAuthenticatedClient(tenantAlpha, TradeFlowRoles.Trader);
        using var riskManagerClient = CreateAuthenticatedClient(customerId: null, TradeFlowRoles.RiskManager);

        var createResponse = await traderAlphaClient.PostAsJsonAsync("/api/orders", new CreateOrderRequest(Currency: "USD"));
        var orderId = await createResponse.Content.ReadFromJsonAsync<Guid>(_jsonOptions);

        // Act: RiskManager cancels Trader Alpha's order due to market volatility
        var cancelResponse = await riskManagerClient.PostAsJsonAsync(
            $"/api/orders/{orderId}/cancel",
            new CancelOrderRequest("Emergency circuit breaker triggered by Risk Management"));

        // Assert
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SubmitOrder_WhenOrderHasNoLines_ShouldReturnBadRequestProblemDetails()
    {
        // Arrange: Create a draft order with zero lines
        using var traderClient = CreateAuthenticatedClient(Guid.NewGuid(), TradeFlowRoles.Trader);
        var createResponse = await traderClient.PostAsJsonAsync("/api/orders", new CreateOrderRequest(Currency: "USD"));

        var orderId = await createResponse.Content.ReadFromJsonAsync<Guid>(_jsonOptions);

        // Act: Attempt to submit empty order
        var submitResponse = await traderClient.PostAsync($"/api/orders/{orderId}/submit", null);

        // Assert: Invariant violation rejected with RFC 7807 problem details
        submitResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        submitResponse.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problemDetails = await submitResponse.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        problemDetails.GetProperty("status").GetInt32().Should().Be(400);
        problemDetails.GetProperty("code").GetString().Should().Be("ORDER_EMPTY_LINES");
    }

    [Fact]
    public async Task FullOrderLifecycle_ShouldCompleteSuccessfully()
    {
        // 1. Create Draft Order as authenticated Trader
        using var traderClient = CreateAuthenticatedClient(Guid.NewGuid(), TradeFlowRoles.Trader);
        var createResponse = await traderClient.PostAsJsonAsync("/api/orders", new CreateOrderRequest(
            Currency: "USD",
            MinimumOrderValue: 100m));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderId = await createResponse.Content.ReadFromJsonAsync<Guid>(_jsonOptions);

        var productId = Guid.NewGuid();

        // Seed inventory stock for product to allow successful reservation on submit
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var inventoryItem = InventoryItem.Create(new ProductId(productId), "SKU-VALVE-01", initialQuantity: 100);
            db.InventoryItems.Add(inventoryItem);
            await db.SaveChangesAsync();
        }

        // 2. Add Line Item
        var lineResponse = await traderClient.PostAsJsonAsync($"/api/orders/{orderId}/lines", new AddOrderLineRequest(
            ProductId: productId,
            Sku: "SKU-VALVE-01",
            ProductName: "Industrial Valve",
            UnitPrice: 150m,
            Quantity: 1));
        lineResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // 3. Submit Order (triggers invariant validation + inventory reservation)
        var submitResponse = await traderClient.PostAsync($"/api/orders/{orderId}/submit", null);
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
