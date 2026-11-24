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
using TradeFlow.Application.Catalog.Queries;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Common.Security;
using Xunit;

namespace TradeFlow.Api.IntegrationTests.Endpoints;

public class CatalogEndpointTests
{
    private static WebApplicationFactory<Program> CreateInMemoryApiFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", $"TradeFlow_Catalog_{Guid.NewGuid()}");
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
    public async Task CreateProduct_AsTrader_ShouldReturn403Forbidden()
    {
        await using var factory = CreateInMemoryApiFactory();
        using var client = factory.CreateClient();
        var traderToken = TestJwtTokenGenerator.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), TradeFlowRoles.Trader);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", traderToken);

        var response = await client.PostAsJsonAsync("/api/catalog/products", new CreateProductRequest(
            "SKU-TEST-001", "Steel Bar", "Ferrous Metals", "Specs", 1200m, "USD"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateSearchUpdateProduct_AsAdminAndTrader_ShouldEnforceRbacAndConflictContracts()
    {
        await using var factory = CreateInMemoryApiFactory();
        using var client = factory.CreateClient();
        var adminToken = TestJwtTokenGenerator.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), TradeFlowRoles.Admin);
        var traderToken = TestJwtTokenGenerator.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), TradeFlowRoles.Trader);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // 1. Admin creates a product -> 201 Created
        var createResp = await client.PostAsJsonAsync("/api/catalog/products", new CreateProductRequest(
            "SKU-ALUM-7075",
            "Aerospace Aluminum Sheet 7075-T6",
            "Non-Ferrous Metals",
            "High-strength aerospace alloy.",
            3450m,
            "USD",
            "Metric Ton",
            2));

        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var productId = await createResp.Content.ReadFromJsonAsync<Guid>();
        productId.Should().NotBeEmpty();

        // 2. Duplicate SKU -> 409 Conflict
        var dupResp = await client.PostAsJsonAsync("/api/catalog/products", new CreateProductRequest(
            "sku-alum-7075",
            "Duplicate",
            "Non-Ferrous Metals",
            "Specs",
            3000m,
            "USD"));

        dupResp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 3. Trader searches catalog -> 200 OK with paged result
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", traderToken);
        var listResp = await client.GetAsync("/api/catalog/products?search=aerospace&category=Non-Ferrous%20Metals");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await listResp.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>();
        page.Should().NotBeNull();
        page!.TotalCount.Should().Be(1);
        page.Items.Single().Sku.Should().Be("SKU-ALUM-7075");

        // 4. Admin updates price -> 200 OK & subsequent GET reflects updated price (cache invalidated)
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var getBefore = await client.GetFromJsonAsync<ProductResponse>($"/api/catalog/products/{productId}");
        getBefore!.UnitPrice.Should().Be(3450m);

        var updateResp = await client.PutAsJsonAsync($"/api/catalog/products/{productId}", new UpdateProductRequest(
            "Aerospace Aluminum Sheet 7075-T6 Rev B",
            "Non-Ferrous Metals",
            "Updated aerospace alloy spec.",
            3600m,
            "USD",
            "Metric Ton",
            2,
            IsActive: true));
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var getAfter = await client.GetFromJsonAsync<ProductResponse>($"/api/catalog/products/{productId}");
        getAfter!.UnitPrice.Should().Be(3600m);
        getAfter.Name.Should().Be("Aerospace Aluminum Sheet 7075-T6 Rev B");
    }
}
