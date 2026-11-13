using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Timeout;
using TradeFlow.Application.Common.Security;
using TradeFlow.Infrastructure.Diagnostics;
using TradeFlow.Infrastructure.Identity;

namespace TradeFlow.Api.Common.Resilience;

public static class TradeFlowResilienceConstants
{
    public const string KeycloakPipelineName = "Keycloak";
    public const string TenantOrderPolicy = "TenantOrderPolicy";
    public const string AuthBruteForcePolicy = "AuthBruteForcePolicy";
}

/// <summary>
/// Configures Phase 8 Inbound Edge Protection (Load Shedding &amp; Distributed Multi-Tenant Rate Limiting)
/// and Outbound HTTP Resilience Pipelines with Polly v8 (ADR-0016).
/// </summary>
public static class ResilienceConfiguration
{
    public static IServiceCollection AddTradeFlowResilience(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        // Each external dependency is registered as an isolated Typed HttpClient with its own named resilience profile.
        // Future external integrations (e.g., PaymentGateway, CreditBureau, TaxEngine) simply add:
        // services.AddHttpClient<IPaymentGatewayService, StripePaymentGatewayService>().AddTradeFlowResiliencePipeline("PaymentGateway");
        services.AddHttpClient<IIdentityProviderService, KeycloakIdentityProviderService>()
            .AddTradeFlowResiliencePipeline(TradeFlowResilienceConstants.KeycloakPipelineName);

        return services;
    }

    /// <summary>
    /// Attaches an isolated 5-stage Polly v8 resilience pipeline to a Typed or Named <see cref="IHttpClientBuilder"/>,
    /// reading thresholds from <c>Resilience:{pipelineName}:*</c> in <see cref="IConfiguration"/>.
    /// </summary>
    public static IHttpClientBuilder AddTradeFlowResiliencePipeline(
        this IHttpClientBuilder clientBuilder,
        string pipelineName)
    {
        clientBuilder.AddResilienceHandler($"{pipelineName.ToLowerInvariant()}-pipeline", (builder, context) =>
        {
            var config = context.ServiceProvider.GetRequiredService<IConfiguration>();
            var timeProvider = context.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;

            ConfigureHttpResiliencePipeline(builder, config, pipelineName, timeProvider);
        });

        return clientBuilder;
    }

    /// <summary>
    /// Configures the 5-stage Polly v8 resilience pipeline for a specific external dependency profile (<paramref name="pipelineName"/>),
    /// ordered from outermost to innermost:
    /// Total Timeout -> Bulkhead Concurrency -> Retry (with Exponential Backoff + Jitter) -> Circuit Breaker -> Attempt Timeout.
    /// </summary>
    public static void ConfigureHttpResiliencePipeline(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        IConfiguration configuration,
        string pipelineName,
        TimeProvider? timeProvider = null)
    {
        builder.TimeProvider = timeProvider ?? TimeProvider.System;

        var sectionPrefix = $"Resilience:{pipelineName}";
        var totalTimeoutSeconds = configuration.GetValue($"{sectionPrefix}:TotalTimeoutSeconds", 10.0);
        var bulkheadPermitLimit = configuration.GetValue($"{sectionPrefix}:BulkheadPermitLimit", 20);
        var bulkheadQueueLimit = configuration.GetValue($"{sectionPrefix}:BulkheadQueueLimit", 10);
        var maxRetries = configuration.GetValue($"{sectionPrefix}:MaxRetries", 2);
        var retryBaseDelayMs = configuration.GetValue($"{sectionPrefix}:RetryBaseDelayMs", 200);
        var failureRatio = configuration.GetValue($"{sectionPrefix}:CircuitBreakerFailureRatio", 0.5);
        var minThroughput = configuration.GetValue($"{sectionPrefix}:CircuitBreakerMinimumThroughput", 5);
        var samplingDurationSeconds = configuration.GetValue($"{sectionPrefix}:CircuitBreakerSamplingSeconds", 30.0);
        var breakDurationSeconds = configuration.GetValue($"{sectionPrefix}:CircuitBreakerBreakSeconds", 15.0);
        var attemptTimeoutSeconds = configuration.GetValue($"{sectionPrefix}:AttemptTimeoutSeconds", 3.0);

        // 1. Outer Total Timeout across all retries and queued waits
        builder.AddTimeout(new HttpTimeoutStrategyOptions
        {
            Name = $"{pipelineName}-TotalTimeout",
            Timeout = TimeSpan.FromSeconds(totalTimeoutSeconds)
        });

        // 2. Bulkhead Isolation (Concurrency Limiter) to prevent downstream slowdowns from starving API threads
        builder.AddConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = bulkheadPermitLimit,
            QueueLimit = bulkheadQueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });

        // 3. Retry with Exponential Backoff + Jitter (strictly for transient errors, never 400/401/403)
        if (maxRetries > 0)
        {
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                Name = $"{pipelineName}-TransientRetry",
                MaxRetryAttempts = maxRetries,
                Delay = TimeSpan.FromMilliseconds(retryBaseDelayMs),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = static args => ValueTask.FromResult(IsTransientOutcome(args.Outcome))
            });
        }

        // 4. Stateful Circuit Breaker (trips to Open when failure ratio exceeds threshold)
        builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            Name = $"{pipelineName}-CircuitBreaker",
            FailureRatio = failureRatio,
            MinimumThroughput = minThroughput,
            SamplingDuration = TimeSpan.FromSeconds(samplingDurationSeconds),
            BreakDuration = TimeSpan.FromSeconds(breakDurationSeconds),
            ShouldHandle = static args => ValueTask.FromResult(IsTransientOutcome(args.Outcome))
        });

        // 5. Inner Per-Attempt Timeout (counts as a failure in the Circuit Breaker if breached)
        builder.AddTimeout(new HttpTimeoutStrategyOptions
        {
            Name = $"{pipelineName}-AttemptTimeout",
            Timeout = TimeSpan.FromSeconds(attemptTimeoutSeconds)
        });
    }

    public static void ConfigureKeycloakResiliencePipeline(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        IConfiguration configuration,
        TimeProvider? timeProvider = null) =>
        ConfigureHttpResiliencePipeline(builder, configuration, TradeFlowResilienceConstants.KeycloakPipelineName, timeProvider);

    /// <summary>
    /// Evaluates whether an HTTP outcome represents a transient infrastructure fault worthy of retry / circuit breaking.
    /// Deterministic client errors (e.g., 400 Bad Request, 401 Unauthorized, 403 Forbidden) return false.
    /// </summary>
    public static bool IsTransientOutcome(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is HttpRequestException or TimeoutRejectedException)
        {
            return true;
        }

        if (outcome.Result is { } response)
        {
            var statusCode = (int)response.StatusCode;
            return statusCode >= 500 ||
                   response.StatusCode == HttpStatusCode.RequestTimeout ||
                   response.StatusCode == HttpStatusCode.TooManyRequests;
        }

        return false;
    }

    public static IApplicationBuilder UseTradeFlowResilience(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TradeFlowResilienceMiddleware>();
    }
}
