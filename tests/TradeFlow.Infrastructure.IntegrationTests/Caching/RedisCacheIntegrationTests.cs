using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;
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

    private sealed record TestOrderSummary(Guid OrderId, string Currency, decimal Total);
}
