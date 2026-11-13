using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Polly;
using Polly.CircuitBreaker;
using TradeFlow.Api.Common.Resilience;
using TradeFlow.Api.Endpoints;
using TradeFlow.Api.IntegrationTests.Infrastructure;
using TradeFlow.Application.Common.Security;
using TradeFlow.Infrastructure.Caching;
using Xunit;

namespace TradeFlow.Api.IntegrationTests.Resilience;

/// <summary>
/// ADR-0016: Verifies Phase 8 Polly v8 outbound resilience pipelines (using FakeTimeProvider)
/// and inbound Edge Protection (Multi-Tenant Rate Limiting, Noisy Neighbor Isolation, and Load Shedding).
/// </summary>
public class ResilienceAndRateLimitingTests
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private static WebApplicationFactory<Program> CreateTestApiFactory(Action<IWebHostBuilder>? configure = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", $"TradeFlow_Resilience_{Guid.NewGuid()}");
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

            configure?.Invoke(builder);
        });
    }

    private static HttpClient CreateAuthenticatedClient(
        WebApplicationFactory<Program> factory,
        Guid? customerId,
        params string[] roles)
    {
        var client = factory.CreateClient();
        var token = TestJwtTokenGenerator.GenerateToken(Guid.NewGuid(), customerId, roles);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task KeycloakPipeline_WhenResponseIs401Unauthorized_ShouldNeverRetry()
    {
        // Arrange: 401 is a deterministic client/credential error and must NEVER trigger retries
        var fakeTime = new FakeTimeProvider();
        var attemptCount = 0;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Resilience:Keycloak:MaxRetries"] = "3",
                ["Resilience:Keycloak:RetryBaseDelayMs"] = "1"
            })
            .Build();

        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();
        ResilienceConfiguration.ConfigureKeycloakResiliencePipeline(builder, config, fakeTime);
        var pipeline = builder.Build();

        // Act
        var response = await pipeline.ExecuteAsync(_ =>
        {
            Interlocked.Increment(ref attemptCount);
            return ValueTask.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        attemptCount.Should().Be(1, "401 Unauthorized is non-transient and must never be retried");
    }

    [Fact]
    public async Task KeycloakPipeline_WhenTransient503OccursTwiceThenSucceeds_ShouldRetryAndReturn200Ok()
    {
        // Arrange
        var attemptCount = 0;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Resilience:Keycloak:MaxRetries"] = "2",
                ["Resilience:Keycloak:RetryBaseDelayMs"] = "1",
                ["Resilience:Keycloak:CircuitBreakerMinimumThroughput"] = "10"
            })
            .Build();

        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();
        ResilienceConfiguration.ConfigureKeycloakResiliencePipeline(builder, config);
        var pipeline = builder.Build();

        // Act: First 2 attempts fail with 503 Service Unavailable, 3rd attempt succeeds with 200 OK
        var response = await pipeline.ExecuteAsync(_ =>
        {
            var current = Interlocked.Increment(ref attemptCount);
            var status = current <= 2 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
            return ValueTask.FromResult(new HttpResponseMessage(status));
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        attemptCount.Should().Be(3, "1 initial attempt + 2 transient retries = 3 total executions");
    }

    [Fact]
    public async Task KeycloakPipeline_CircuitBreakerStateMachine_ShouldOpenOnSustained503s_FailFast_AndCloseAfterFakeTimeAdvance()
    {
        // Arrange: Use FakeTimeProvider to test 15-second Circuit Breaker BreakDuration in < 5ms without Thread.Sleep!
        var fakeTime = new FakeTimeProvider();
        var attemptCount = 0;
        var shouldFail = true;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Resilience:Keycloak:MaxRetries"] = "0",
                ["Resilience:Keycloak:CircuitBreakerFailureRatio"] = "0.5",
                ["Resilience:Keycloak:CircuitBreakerMinimumThroughput"] = "3",
                ["Resilience:Keycloak:CircuitBreakerSamplingSeconds"] = "30",
                ["Resilience:Keycloak:CircuitBreakerBreakSeconds"] = "15"
            })
            .Build();

        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();
        ResilienceConfiguration.ConfigureKeycloakResiliencePipeline(builder, config, fakeTime);
        var pipeline = builder.Build();

        ValueTask<HttpResponseMessage> SimulatedKeycloakCall(CancellationToken _)
        {
            Interlocked.Increment(ref attemptCount);
            return ValueTask.FromResult(new HttpResponseMessage(
                shouldFail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        }

        // Act 1: Send 3 failing requests (reaches MinimumThroughput = 3 with 100% failure ratio -> trips Circuit Breaker to OPEN)
        for (var i = 0; i < 3; i++)
        {
            var res = await pipeline.ExecuteAsync(SimulatedKeycloakCall);
            res.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }

        attemptCount.Should().Be(3);

        // Act 2: 4th request must FAIL FAST with BrokenCircuitException without invoking SimulatedKeycloakCall!
        var actOpen = async () => await pipeline.ExecuteAsync(SimulatedKeycloakCall);
        await actOpen.Should().ThrowAsync<BrokenCircuitException>();
        attemptCount.Should().Be(3, "Circuit is OPEN: downstream handler must not be called!");

        // Act 3: Advance virtual clock by 16 seconds (past the 15s BreakDuration -> transitions to HALF-OPEN)
        fakeTime.Advance(TimeSpan.FromSeconds(16));
        shouldFail = false;

        // Probe request in Half-Open succeeds -> transitions Circuit Breaker back to CLOSED!
        var recoveryResponse = await pipeline.ExecuteAsync(SimulatedKeycloakCall);
        recoveryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        attemptCount.Should().Be(4);

        // Subsequent request flows normally in CLOSED state
        var normalResponse = await pipeline.ExecuteAsync(SimulatedKeycloakCall);
        normalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        attemptCount.Should().Be(5);
    }

    [Fact]
    public async Task InMemoryDistributedRateLimiter_WhenWindowExpiresUsingFakeTimeProvider_ShouldResetCounter()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider();
        var limiter = new InMemoryDistributedRateLimiter(fakeTime);
        const string key = "tenant:alpha";
        var window = TimeSpan.FromSeconds(60);

        // Act 1: Exhaust 2 permits
        (await limiter.TryAcquireAsync(key, permitLimit: 2, window)).IsAllowed.Should().BeTrue();
        (await limiter.TryAcquireAsync(key, permitLimit: 2, window)).IsAllowed.Should().BeTrue();

        // 3rd attempt in same window is rejected
        var rejected = await limiter.TryAcquireAsync(key, permitLimit: 2, window);
        rejected.IsAllowed.Should().BeFalse();

        // Act 2: Advance FakeTimeProvider by 61 seconds -> window resets!
        fakeTime.Advance(TimeSpan.FromSeconds(61));
        var allowedAfterReset = await limiter.TryAcquireAsync(key, permitLimit: 2, window);

        // Assert
        allowedAfterReset.IsAllowed.Should().BeTrue();
        allowedAfterReset.CurrentCount.Should().Be(1);
    }

    [Fact]
    public async Task RateLimiting_WhenTenantAlphaExceedsQuota_ShouldReturn429TooManyRequests_WhileTenantBetaRemainsIsolated()
    {
        // Arrange: Configure low tenant rate limit (3 requests / 60s) to verify noisy neighbor isolation
        await using var customFactory = CreateTestApiFactory(builder =>
        {
            builder.UseSetting("Resilience:RateLimiting:TenantPermitLimit", "3");
            builder.UseSetting("Resilience:RateLimiting:WindowSeconds", "60");
        });

        var tenantAlpha = Guid.NewGuid();
        var tenantBeta = Guid.NewGuid();

        using var alphaClient = CreateAuthenticatedClient(customFactory, tenantAlpha, TradeFlowRoles.Trader);
        using var betaClient = CreateAuthenticatedClient(customFactory, tenantBeta, TradeFlowRoles.Trader);

        // Act 1: Tenant Alpha sends 3 allowed requests
        for (var i = 0; i < 3; i++)
        {
            var okResponse = await alphaClient.PostAsJsonAsync("/api/orders", new CreateOrderRequest(Currency: "USD"));
            okResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            okResponse.Headers.Contains("X-RateLimit-Limit").Should().BeTrue();
        }

        // Act 2: Tenant Alpha sends 4th request -> rejected with 429 Too Many Requests + Retry-After
        var throttledResponse = await alphaClient.PostAsJsonAsync("/api/orders", new CreateOrderRequest(Currency: "USD"));

        // Act 3: Tenant Beta sends request at the exact same moment -> succeeds! (Noisy Neighbor Isolation)
        var betaResponse = await betaClient.PostAsJsonAsync("/api/orders", new CreateOrderRequest(Currency: "USD"));

        // Assert
        throttledResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        throttledResponse.Headers.RetryAfter.Should().NotBeNull();

        var problem = await throttledResponse.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        problem.GetProperty("status").GetInt32().Should().Be(429);
        problem.GetProperty("code").GetString().Should().Be("RATE_LIMIT_EXCEEDED");

        betaResponse.StatusCode.Should().Be(HttpStatusCode.Created, "Tenant Beta must not be affected by Tenant Alpha's rate limit exhaustion");
    }

    [Fact]
    public async Task AuthEndpoint_WhenKeycloakIsDown_ShouldFailFastWith503IdentityProviderUnavailableProblemDetails()
    {
        // Arrange: Point Keycloak authority to an unreachable local port with fast timeout and 0 retries
        await using var customFactory = CreateTestApiFactory(builder =>
        {
            builder.UseSetting("Authentication:Authority", "http://localhost:59999/realms/tradeflow");
            builder.UseSetting("Resilience:Keycloak:MaxRetries", "0");
            builder.UseSetting("Resilience:Keycloak:AttemptTimeoutSeconds", "0.5");
        });

        using var client = customFactory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/token", new TokenLoginRequest("trader.alpha", "trader123"));

        // Assert: Returns 503 Service Unavailable with RFC 7807 ProblemDetails and Retry-After header
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.Should().NotBeNull();

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        problem.GetProperty("status").GetInt32().Should().Be(503);
        problem.GetProperty("code").GetString().Should().Be("IDENTITY_PROVIDER_UNAVAILABLE");
    }
}
