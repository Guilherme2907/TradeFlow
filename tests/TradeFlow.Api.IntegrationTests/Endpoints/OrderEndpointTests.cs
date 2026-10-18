using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TradeFlow.Api.Endpoints;
using TradeFlow.Api.IntegrationTests.Infrastructure;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Inventory;
using TradeFlow.Domain.Orders;
using TradeFlow.Infrastructure.Modules.Inventory.Persistence;
using Xunit;

namespace TradeFlow.Api.IntegrationTests.Endpoints;

[Trait("Category", "RequiresDocker")]
public class OrderEndpointTests : IClassFixture<TradeFlowWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TradeFlowWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public OrderEndpointTests(TradeFlowWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateOrder_WithValidPayload_ShouldReturnCreatedWithLocationAndId()
    {
        // Arrange
        var request = new CreateOrderRequest(
            CustomerId: Guid.NewGuid(),
            Currency: "USD",
            MinimumOrderValue: 200m);

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", request);

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
        var request = new CreateOrderRequest(
            CustomerId: Guid.NewGuid(),
            Currency: "USDD",
            MinimumOrderValue: 100m);

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", request);

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
        var nonExistentOrderId = Guid.NewGuid();
        var request = new AddOrderLineRequest(
            ProductId: Guid.NewGuid(),
            Sku: "SKU-VALVE-01",
            ProductName: "Control Valve",
            UnitPrice: 150m,
            Quantity: 2);

        // Act
        var response = await _client.PostAsJsonAsync($"/api/orders/{nonExistentOrderId}/lines", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problemDetails = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        problemDetails.GetProperty("status").GetInt32().Should().Be(404);
        problemDetails.GetProperty("title").GetString().Should().Be("Not Found");
        problemDetails.GetProperty("code").GetString().Should().Be("ORDER_NOT_FOUND");
    }

    [Fact]
    public async Task SubmitOrder_WhenOrderHasNoLines_ShouldReturnBadRequestProblemDetails()
    {
        // Arrange: Create a draft order with zero lines
        var createResponse = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequest(
            CustomerId: Guid.NewGuid(),
            Currency: "USD"));

        var orderId = await createResponse.Content.ReadFromJsonAsync<Guid>(_jsonOptions);

        // Act: Attempt to submit empty order
        var submitResponse = await _client.PostAsync($"/api/orders/{orderId}/submit", null);

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
        // 1. Create Draft Order
        var createResponse = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequest(
            CustomerId: Guid.NewGuid(),
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
        var lineResponse = await _client.PostAsJsonAsync($"/api/orders/{orderId}/lines", new AddOrderLineRequest(
            ProductId: productId,
            Sku: "SKU-VALVE-01",
            ProductName: "Industrial Valve",
            UnitPrice: 150m,
            Quantity: 1));
        lineResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // 3. Submit Order (triggers invariant validation + inventory reservation)
        var submitResponse = await _client.PostAsync($"/api/orders/{orderId}/submit", null);
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
