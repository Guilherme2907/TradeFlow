using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace TradeFlow.Api.IntegrationTests.Health;

/// <summary>
/// ADR-0017: Verifies Phase 9 Cloud-Native Kubernetes Liveness (<c>/health/live</c>) vs. Readiness (<c>/health/ready</c>)
/// probes and Graceful Shutdown (<see cref="HostOptions.ShutdownTimeout"/>) configuration.
/// </summary>
public class CloudNativeHealthCheckTests
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private static WebApplicationFactory<Program> CreateTestApiFactory(Action<IWebHostBuilder>? configure = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", $"TradeFlow_Health_{Guid.NewGuid()}");
            builder.UseSetting("ConnectionStrings:RabbitMq", string.Empty);
            builder.UseSetting("ConnectionStrings:Kafka", string.Empty);
            builder.UseSetting("ConnectionStrings:Redis", string.Empty);
            builder.UseSetting("Outbox:BackgroundWorkerEnabled", "false");
            builder.UseSetting("Authentication:RequireHttpsMetadata", "false");

            configure?.Invoke(builder);
        });
    }

    [Fact]
    public async Task LivenessProbe_EvenWhenPostgreSqlIsDown_ShouldReturn200OkHealthy()
    {
        // Arrange: Point PostgreSQL to an unreachable port.
        // Liveness (/health/live) must NEVER check the database, preventing Kubernetes restart storms!
        await using var factory = CreateTestApiFactory(builder =>
        {
            builder.UseSetting("UseInMemoryDatabase", "false");
            builder.UseSetting("ConnectionStrings:Database", "Host=localhost;Port=54999;Database=tradeflow;Username=postgres;Password=postgres;Timeout=1");
        });

        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/live");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        report.GetProperty("status").GetString().Should().Be("Healthy");
        report.GetProperty("checks").TryGetProperty("self", out var selfCheck).Should().BeTrue();
        selfCheck.GetProperty("status").GetString().Should().Be("Healthy");
        report.GetProperty("checks").TryGetProperty("postgresql", out _).Should().BeFalse(
            "Liveness probe must never evaluate external database dependencies");
    }

    [Fact]
    public async Task ReadinessProbe_WhenPostgreSqlIsDown_ShouldReturn503ServiceUnavailableUnhealthy()
    {
        // Arrange: Point PostgreSQL to an unreachable port.
        // Readiness (/health/ready) MUST fail with 503 so Kubernetes removes the pod from the Service load balancer.
        await using var factory = CreateTestApiFactory(builder =>
        {
            builder.UseSetting("UseInMemoryDatabase", "false");
            builder.UseSetting("ConnectionStrings:Database", "Host=localhost;Port=54999;Database=tradeflow;Username=postgres;Password=postgres;Timeout=1");
        });

        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/ready");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var report = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        report.GetProperty("status").GetString().Should().Be("Unhealthy");
        report.GetProperty("checks")
            .GetProperty("postgresql")
            .GetProperty("status")
            .GetString()
            .Should()
            .Be("Unhealthy");
    }

    [Fact]
    public async Task ReadinessProbe_WhenDatabaseIsHealthy_ShouldReturn200Ok()
    {
        // Arrange
        await using var factory = CreateTestApiFactory();
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/ready");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        report.GetProperty("checks")
            .GetProperty("postgresql")
            .GetProperty("status")
            .GetString()
            .Should()
            .Be("Healthy");
    }

    [Fact]
    public void GracefulShutdown_ShouldConfigure25SecondShutdownTimeoutMatchingKubernetesGracePeriod()
    {
        // Arrange
        using var factory = CreateTestApiFactory();

        // Act
        var hostOptions = factory.Services.GetRequiredService<IOptions<HostOptions>>().Value;

        // Assert: 25s drain timeout leaves a 5s safety margin before Kubernetes' 30s terminationGracePeriodSeconds
        hostOptions.ShutdownTimeout.Should().Be(TimeSpan.FromSeconds(25));
    }
}
