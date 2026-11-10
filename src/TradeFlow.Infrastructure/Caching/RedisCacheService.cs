using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradeFlow.Application.Common.Caching;

namespace TradeFlow.Infrastructure.Caching;

/// <summary>
/// High-performance distributed cache service backed by Redis 7 (StackExchange.Redis) with:
/// 1. Cache-Aside pattern with JSON serialization.
/// 2. Single-Flight mutex synchronization against Cache Stampede (Dog-piling / Thundering Herd).
/// 3. Randomized TTL Jitter to prevent mass key expiration spikes.
/// 4. Fail-Open graceful degradation when Redis is temporarily unreachable (ADR-0014).
/// </summary>
public sealed class RedisCacheService : ICacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ResultJsonConverterFactory() }
    };
    public static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(5);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public RedisCacheService(
        IConnectionMultiplexer redis,
        ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    private IDatabase? GetDatabaseSafe()
    {
        try
        {
            return _redis.GetDatabase();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Redis Warning] Failed to acquire Redis database. Operating in Fail-Open mode.");
            return null;
        }
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var (found, value) = await TryGetAsync<T>(key, cancellationToken);
        return found ? value : default;
    }

    private async Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken cancellationToken)
    {
        var db = GetDatabaseSafe();
        if (db is null)
        {
            return (false, default);
        }

        try
        {
            var value = await db.StringGetAsync(key);
            if (value.IsNullOrEmpty)
            {
                return (false, default);
            }

            var deserialized = JsonSerializer.Deserialize<T>((string)value!, SerializerOptions);
            return (true, deserialized);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Redis Warning] Failed to read key '{CacheKey}'. Falling back to primary source.", key);
            return (false, default);
        }
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var db = GetDatabaseSafe();
        if (db is null)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(value, SerializerOptions);
            var effectiveTtl = ApplyJitter(expiration ?? DefaultExpiration);

            await db.StringSetAsync(key, json, effectiveTtl);
            _logger.LogDebug("[Redis Set] Cached key '{CacheKey}' with TTL {TtlSeconds}s", key, effectiveTtl.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Redis Warning] Failed to write key '{CacheKey}' to Redis. Operating in Fail-Open mode.", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var db = GetDatabaseSafe();
        if (db is null)
        {
            return;
        }

        try
        {
            await db.KeyDeleteAsync(key);
            _logger.LogDebug("[Redis Evict] Evicted cache key '{CacheKey}'", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Redis Warning] Failed to evict key '{CacheKey}' from Redis.", key);
        }
    }

    public async Task RemoveByPrefixAsync(string prefixKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefixKey);

        try
        {
            var endpoints = _redis.GetEndPoints();
            var db = _redis.GetDatabase();

            foreach (var endpoint in endpoints)
            {
                var server = _redis.GetServer(endpoint);
                if (!server.IsConnected)
                {
                    continue;
                }

                // Non-blocking SCAN cursor iteration (avoids production performance traps of 'KEYS *')
                await foreach (var key in server.KeysAsync(pattern: $"{prefixKey}*").WithCancellation(cancellationToken))
                {
                    await db.KeyDeleteAsync(key);
                }
            }

            _logger.LogDebug("[Redis Evict] Evicted keys matching prefix '{Prefix}*'", prefixKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Redis Warning] Failed to evict keys matching prefix '{Prefix}*'.", prefixKey);
        }
    }

    public async Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        Func<T, bool>? shouldCache = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        // Step 1: Fast Path — Direct Cache Read without synchronization
        var (found, cached) = await TryGetAsync<T>(key, cancellationToken);
        if (found)
        {
            return cached;
        }

        // Step 2: Cache Miss — Coordinate single-flight execution via keyed Semaphore to eliminate Thundering Herd
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            // Step 3: Double-Check — Did another concurrent thread populate the cache while we awaited the lock?
            (found, cached) = await TryGetAsync<T>(key, cancellationToken);
            if (found)
            {
                return cached;
            }

            // Step 4: Single Flight — Only 1 request hits the primary datastore (e.g., PostgreSQL)
            var fresh = await factory(cancellationToken);

            // Step 5: Store in Cache if condition matches
            if (shouldCache is null || shouldCache(fresh))
            {
                await SetAsync(key, fresh, expiration, cancellationToken);
            }

            return fresh;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Applies 5% to 10% randomized jitter to base TTL to prevent mass expiration spikes (ADR-0014).
    /// </summary>
    public static TimeSpan ApplyJitter(TimeSpan baseTtl)
    {
        if (baseTtl <= TimeSpan.Zero)
        {
            return baseTtl;
        }

        var jitterPercent = RandomNumberGenerator.GetInt32(5, 11) / 100.0;
        var jitterMs = (int)(baseTtl.TotalMilliseconds * jitterPercent);
        return baseTtl.Add(TimeSpan.FromMilliseconds(jitterMs));
    }
}
