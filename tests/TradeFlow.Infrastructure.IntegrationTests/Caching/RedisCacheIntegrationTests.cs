using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Orders.Queries.GetOrderById;
using TradeFlow.Infrastructure.Caching;
using Xunit;

namespace TradeFlow.Infrastructure.IntegrationTests.Caching;

[Trait("Category", "RequiresDocker")]
public class RedisCacheIntegrationTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:7-alpine")
        .Build();

    private IConnectionMultiplexer _connectionMultiplexer = null!;
    private RedisCacheService _cacheService = null!;

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();
        _connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
        _cacheService = new RedisCacheService(_connectionMultiplexer, NullLogger<RedisCacheService>.Instance);
    }

    public async Task DisposeAsync()
    {
        _connectionMultiplexer.Dispose();
        await _redisContainer.DisposeAsync();
    }

    [Fact]
    public async Task GetOrCreateAsync_When50ConcurrentRequestsHitMissingKey_ShouldExecuteFactoryOnlyOnce_PreventingCacheStampede()
    {
        // Arrange: 50 concurrent requests asking for the same missing key simultaneously
        const string hotKey = "test:stampede:quote:copper";
        var factoryExecutionCount = 0;

        async Task<decimal> ExpensivePriceLookupAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref factoryExecutionCount);
            // Simulate database latency of 50ms
            await Task.Delay(50, ct);
            return 8500.50m;
        }

        // Act: Fire 50 simultaneous tasks simulating a Thundering Herd / Dog-Piling stampede
        var tasks = Enumerable.Range(0, 50)
            .Select(_ => _cacheService.GetOrCreateAsync(
                hotKey,
                ExpensivePriceLookupAsync,
                TimeSpan.FromMinutes(5)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert: All 50 callers received the correct price, but the database factory was executed EXACTLY ONCE!
        results.Should().HaveCount(50);
        results.Should().AllBeEquivalentTo(8500.50m);
        factoryExecutionCount.Should().Be(1, "Single-flight lock must collapse 50 concurrent misses into exactly 1 database execution!");

        // Subsequent direct read must be a cache hit
        var directHit = await _cacheService.GetAsync<decimal>(hotKey);
        directHit.Should().Be(8500.50m);
    }

    [Fact]
    public async Task GetAsync_And_SetAsync_ShouldStoreAndRetrieveValuesCorrectly()
    {
        // Arrange
        const string key = "test:orders:item-999";
        var payload = new TestOrderSummary(Guid.NewGuid(), "USD", 12500m);

        // Act
        await _cacheService.SetAsync(key, payload, TimeSpan.FromMinutes(2));
        var retrieved = await _cacheService.GetAsync<TestOrderSummary>(key);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.OrderId.Should().Be(payload.OrderId);
        retrieved.Currency.Should().Be(payload.Currency);
        retrieved.Total.Should().Be(payload.Total);
    }

    [Fact]
    public async Task RemoveByPrefixAsync_ShouldEvictAllMatchingKeys()
    {
        // Arrange
        const string prefix = "test:catalog:category-metals:";
        await _cacheService.SetAsync($"{prefix}copper", 100m);
        await _cacheService.SetAsync($"{prefix}zinc", 200m);
        await _cacheService.SetAsync("test:catalog:category-energy:oil", 300m);

        // Act
        await _cacheService.RemoveByPrefixAsync(prefix);

        // Assert
        var copper = await _cacheService.GetAsync<decimal>($"{prefix}copper");
        var zinc = await _cacheService.GetAsync<decimal>($"{prefix}zinc");
        var oil = await _cacheService.GetAsync<decimal>("test:catalog:category-energy:oil");

        copper.Should().Be(default);
        zinc.Should().Be(default);
        oil.Should().Be(300m, "Unrelated prefixes must not be evicted");
    }

    [Fact]
    public async Task GetOrCreateAsync_WithResultOrderResponse_ShouldUnwrapJsonInRedisAndDeserializeOnCacheHit()
    {
        // Arrange: Simulate the exact TResponse used by CachingBehavior<GetOrderByIdQuery, Result<OrderResponse>>
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var cacheKey = $"orders:{orderId}:tenant:{customerId}";
        var factoryCalls = 0;

        var orderDto = new OrderResponse(
            OrderId: orderId,
            CustomerId: customerId,
            Status: "Submitted",
            Currency: "USD",
            TotalAmount: 860.00m,
            Lines:
            [
                new OrderLineResponse(Guid.NewGuid(), Guid.NewGuid(), "SKU-STEEL-001", "Steel Sheets", 250m, "USD", 2, 500m),
                new OrderLineResponse(Guid.NewGuid(), Guid.NewGuid(), "SKU-COPPER-002", "Copper Rods", 120m, "USD", 3, 360m)
            ],
            CreatedAt: DateTimeOffset.UtcNow);

        Task<Result<OrderResponse>> FactoryAsync(CancellationToken _)
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult(Result.Success(orderDto));
        }

        // Act 1: First call (Cache Miss -> executes factory -> serializes to Redis)
        var firstCall = await _cacheService.GetOrCreateAsync(
            cacheKey,
            FactoryAsync,
            TimeSpan.FromMinutes(5),
            shouldCache: res => res is not Result { IsFailure: true });

        // Act 2: Second call (Cache Hit -> deserializes Result<OrderResponse> from Redis without calling factory)
        var secondCall = await _cacheService.GetOrCreateAsync(
            cacheKey,
            FactoryAsync,
            TimeSpan.FromMinutes(5),
            shouldCache: res => res is not Result { IsFailure: true });

        // Assert
        factoryCalls.Should().Be(1, "Second call must be served from Redis cache without hitting the database factory!");
        firstCall.Should().NotBeNull();
        secondCall.Should().NotBeNull();
        secondCall!.IsSuccess.Should().BeTrue();
        secondCall.Value.OrderId.Should().Be(orderId);
        secondCall.Value.TotalAmount.Should().Be(860.00m);
        secondCall.Value.Lines.Should().HaveCount(2);

        // Verify raw JSON stored in Redis is the unwrapped OrderResponse (no 'isSuccess' or 'error' wrapper pollution)
        var rawJson = (string?)await _connectionMultiplexer.GetDatabase().StringGetAsync(cacheKey);
        rawJson.Should().NotBeNullOrWhiteSpace();
        rawJson.Should().Contain("\"orderId\":");
        rawJson.Should().NotContain("\"isSuccess\":");
    }

    private sealed record TestOrderSummary(Guid OrderId, string Currency, decimal Total);
}
