using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TradeFlow.Application.Common.Caching;

namespace TradeFlow.Api.Common.Resilience;

/// <summary>
/// Enforces inbound Load Shedding (global concurrency protection) and context-aware
/// Distributed Rate Limiting partitioned by B2B Tenant (<c>customer_id</c>) or Client IP (ADR-0016).
/// </summary>
public sealed class TradeFlowResilienceMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly IDistributedRateLimiter _rateLimiter;
    private readonly ILogger<TradeFlowResilienceMiddleware> _logger;
    private readonly SemaphoreSlim _loadSheddingGate;
    private readonly int _maxConcurrentRequests;

    public TradeFlowResilienceMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        IDistributedRateLimiter rateLimiter,
        ILogger<TradeFlowResilienceMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _rateLimiter = rateLimiter;
        _logger = logger;
        _maxConcurrentRequests = configuration.GetValue("Resilience:LoadShedding:MaxConcurrentRequests", 200);
        _loadSheddingGate = new SemaphoreSlim(_maxConcurrentRequests, _maxConcurrentRequests);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 1. Global Load Shedding: protect server survival under extreme concurrency spikes
        if (!_loadSheddingGate.Wait(0))
        {
            Activity.Current?.SetTag("resilience.load_shedding", true);
            Activity.Current?.SetStatus(ActivityStatusCode.Error, "Load shedding triggered: maximum concurrent requests exceeded.");

            _logger.LogWarning(
                "[Load Shedding] Rejected inbound request {Method} {Path}. Active concurrent requests reached ceiling ({MaxConcurrent}).",
                context.Request.Method,
                context.Request.Path,
                _maxConcurrentRequests);

            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "5";
            await context.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Title = "Service Unavailable — Load Shedding Active",
                    Detail = "The server has reached its maximum safe concurrent request capacity. Please retry shortly.",
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.6.4",
                    Extensions = { ["code"] = "LOAD_SHEDDING_REJECTED" }
                },
                options: null,
                contentType: "application/problem+json",
                cancellationToken: context.RequestAborted);
            return;
        }

        try
        {
            // 2. Context-Aware Distributed Rate Limiting
            var path = context.Request.Path.Value ?? string.Empty;
            if (TryResolveRateLimitPolicy(context, path, out var partitionKey, out var permitLimit, out var window))
            {
                var lease = await _rateLimiter.TryAcquireAsync(partitionKey, permitLimit, window, context.RequestAborted);

                context.Response.Headers["X-RateLimit-Limit"] = permitLimit.ToString();
                context.Response.Headers["X-RateLimit-Remaining"] = Math.Max(0, permitLimit - lease.CurrentCount).ToString();

                if (!lease.IsAllowed)
                {
                    var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(lease.RetryAfter.TotalSeconds));
                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.Response.Headers.RetryAfter = retryAfterSeconds.ToString();

                    Activity.Current?.SetTag("resilience.rate_limited", true);
                    Activity.Current?.SetTag("resilience.partition_key", partitionKey);
                    Activity.Current?.SetStatus(ActivityStatusCode.Error, $"Rate limit exceeded for partition '{partitionKey}'.");

                    _logger.LogWarning(
                        "[Rate Limiter] Partition '{PartitionKey}' exceeded quota ({CurrentCount}/{PermitLimit}). Rejecting with 429 Too Many Requests.",
                        partitionKey,
                        lease.CurrentCount,
                        permitLimit);

                    await context.Response.WriteAsJsonAsync(
                        new ProblemDetails
                        {
                            Status = StatusCodes.Status429TooManyRequests,
                            Title = "Too Many Requests",
                            Detail = $"Rate limit quota of {permitLimit} requests per {window.TotalSeconds:0}s exceeded. Please retry after {retryAfterSeconds} seconds.",
                            Type = "https://tools.ietf.org/html/rfc6585#section-4",
                            Extensions =
                            {
                                ["code"] = "RATE_LIMIT_EXCEEDED",
                                ["retryAfterSeconds"] = retryAfterSeconds
                            }
                        },
                        options: null,
                        contentType: "application/problem+json",
                        cancellationToken: context.RequestAborted);
                    return;
                }
            }

            await _next(context);
        }
        finally
        {
            _loadSheddingGate.Release();
        }
    }

    private bool TryResolveRateLimitPolicy(
        HttpContext context,
        string path,
        out string partitionKey,
        out int permitLimit,
        out TimeSpan window)
    {
        var windowSeconds = _configuration.GetValue("Resilience:RateLimiting:WindowSeconds", 60);
        window = TimeSpan.FromSeconds(Math.Max(1, windowSeconds));

        if (path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase))
        {
            var clientIp = ResolveClientIp(context);
            partitionKey = $"auth-ip:{clientIp}";
            permitLimit = _configuration.GetValue("Resilience:RateLimiting:AuthPermitLimit", 15);
            return true;
        }

        if (path.StartsWith("/api/orders", StringComparison.OrdinalIgnoreCase))
        {
            partitionKey = ResolveAuthenticatedPartitionKey(context);
            permitLimit = _configuration.GetValue("Resilience:RateLimiting:TenantPermitLimit", 100);
            return true;
        }

        partitionKey = string.Empty;
        permitLimit = 0;
        return false;
    }

    private static string ResolveAuthenticatedPartitionKey(HttpContext context)
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var customerId = user.FindFirstValue("customer_id");
            if (!string.IsNullOrWhiteSpace(customerId))
            {
                return $"tenant:{customerId}";
            }

            var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
            if (!string.IsNullOrWhiteSpace(sub))
            {
                return $"user:{sub}";
            }
        }

        return $"anon-ip:{ResolveClientIp(context)}";
    }

    private static string ResolveClientIp(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
