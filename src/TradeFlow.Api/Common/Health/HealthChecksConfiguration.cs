using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using TradeFlow.Infrastructure.Modules.Inventory.Persistence;
using TradeFlow.Infrastructure.Modules.Orders.Persistence;

namespace TradeFlow.Api.Common.Health;

/// <summary>
/// Configures Cloud-Native Kubernetes Liveness (<c>/health/live</c>) and Readiness (<c>/health/ready</c>) probes
/// as well as Graceful Shutdown drain timeouts (ADR-0017).
/// </summary>
public static class HealthChecksConfiguration
{
    public const string LiveTag = "live";
    public const string ReadyTag = "ready";

    public static IServiceCollection AddTradeFlowHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure Graceful Shutdown timeout (25s) to complete in-flight HTTP requests and drain
        // BackgroundServices before Kubernetes sends SIGKILL after terminationGracePeriodSeconds (30s).
        var shutdownTimeoutSeconds = configuration.GetValue("Host:ShutdownTimeoutSeconds", 25);
        services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(Math.Max(1, shutdownTimeoutSeconds));
        });

        services.AddHealthChecks()
            .AddCheck(
                name: "self",
                check: () => HealthCheckResult.Healthy("TradeFlow API process is alive and responsive."),
                tags: [LiveTag])
            .AddTypeActivatedCheck<PostgreSqlReadinessHealthCheck>(
                name: "postgresql",
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadyTag])
            .AddTypeActivatedCheck<RedisFailOpenReadinessHealthCheck>(
                name: "redis",
                failureStatus: HealthStatus.Degraded,
                tags: [ReadyTag]);

        return services;
    }

    public static IEndpointRouteBuilder MapTradeFlowHealthChecks(this IEndpointRouteBuilder app)
    {
        // 1. Liveness Probe: ONLY checks if the .NET process itself is responsive.
        // Never checks external databases/brokers to avoid cascading pod restart storms!
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = static check => check.Tags.Contains(LiveTag),
            ResponseWriter = WriteHealthReportJsonAsync
        }).AllowAnonymous();

        // 2. Readiness Probe: Checks critical infrastructure dependencies before accepting Load Balancer traffic.
        // - PostgreSQL failure -> Unhealthy (503 Service Unavailable) -> removes pod from Service endpoints.
        // - Redis failure -> Degraded (200 OK) -> pod remains in Service using Fail-Open fallback while alerting SRE.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = static check => check.Tags.Contains(ReadyTag),
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            },
            ResponseWriter = WriteHealthReportJsonAsync
        }).AllowAnonymous();

        return app;
    }

    private static Task WriteHealthReportJsonAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            checks = report.Entries.ToDictionary(
                static entry => entry.Key,
                static entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 2),
                    error = entry.Value.Exception?.Message
                })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}

/// <summary>
/// Verifies that both module DbContexts (<see cref="OrdersDbContext"/> and <see cref="InventoryDbContext"/>)
/// can connect to PostgreSQL. If PostgreSQL is down, returns <see cref="HealthStatus.Unhealthy"/> (HTTP 503).
/// </summary>
public sealed class PostgreSqlReadinessHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var inventoryDb = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

            var canConnectOrders = await ordersDb.Database.CanConnectAsync(cancellationToken);
            var canConnectInventory = await inventoryDb.Database.CanConnectAsync(cancellationToken);

            if (canConnectOrders && canConnectInventory)
            {
                return HealthCheckResult.Healthy("PostgreSQL orders and inventory schemas are reachable.");
            }

            return HealthCheckResult.Unhealthy("PostgreSQL database connection check returned false.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL database is unreachable.", ex);
        }
    }
}

/// <summary>
/// Verifies Redis connectivity. Because TradeFlow caching (ADR-0014) and rate limiting (ADR-0016) are
/// designed as Fail-Open with in-memory fallback, a Redis outage returns <see cref="HealthStatus.Degraded"/>
/// (HTTP 200 OK) rather than <see cref="HealthStatus.Unhealthy"/> so pods continue serving core order traffic.
/// </summary>
public sealed class RedisFailOpenReadinessHealthCheck(IServiceProvider serviceProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var multiplexer = serviceProvider.GetService<IConnectionMultiplexer>();
        if (multiplexer is null)
        {
            return HealthCheckResult.Healthy("Redis is not configured; operating with in-memory cache and rate limiter.");
        }

        if (!multiplexer.IsConnected)
        {
            return HealthCheckResult.Degraded(
                "Redis is disconnected; operating in Fail-Open in-memory fallback mode (ADR-0014 / ADR-0016).");
        }

        try
        {
            var latency = await multiplexer.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"Redis is reachable (ping: {latency.TotalMilliseconds:0.##} ms).");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded(
                "Redis ping failed; operating in Fail-Open in-memory fallback mode.",
                ex);
        }
    }
}
