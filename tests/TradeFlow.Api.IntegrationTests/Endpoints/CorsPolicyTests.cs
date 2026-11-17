using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TradeFlow.Api.IntegrationTests.Endpoints;

public class CorsPolicyTests
{
    private static WebApplicationFactory<Program> CreateTestApiFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", $"TradeFlow_Cors_{Guid.NewGuid()}");
            builder.UseSetting("ConnectionStrings:RabbitMq", string.Empty);
            builder.UseSetting("ConnectionStrings:Kafka", string.Empty);
            builder.UseSetting("ConnectionStrings:Redis", string.Empty);
            builder.UseSetting("Outbox:BackgroundWorkerEnabled", "false");
            builder.UseSetting("Authentication:RequireHttpsMetadata", "false");
            builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:5173");
        });
    }

    [Fact]
    public async Task PreflightOptions_FromAllowedFrontendOrigin_ShouldReturnCorsAllowOriginHeader()
    {
        await using var factory = CreateTestApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/orders");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values).Should().BeTrue();
        values.Should().Contain("http://localhost:5173");
    }

    [Fact]
    public async Task PreflightOptions_FromUntrustedOrigin_ShouldNotReturnCorsAllowOriginHeader()
    {
        await using var factory = CreateTestApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/orders");
        request.Headers.Add("Origin", "https://malicious-site.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }
}
