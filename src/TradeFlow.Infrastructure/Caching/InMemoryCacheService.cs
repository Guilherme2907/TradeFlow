using System.Collections.Concurrent;
using TradeFlow.Application.Common.Caching;

namespace TradeFlow.Infrastructure.Caching;

/// <summary>
/// Thread-safe in-memory cache implementation used for unit testing, API test isolation,
/// or development environments without an active Redis instance.
/// </summary>
public sealed class InMemoryCacheService : ICacheService
{
    private sealed record CacheEntry(object? Value, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, CacheEntry> _storage = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    private (bool Found, T? Value) TryGet<T>(string key)
    {
        if (_storage.TryGetValue(key, out var entry))
        {
            if (DateTimeOffset.UtcNow <= entry.ExpiresAt)
            {
                return (true, (T?)entry.Value);
            }

            _storage.TryRemove(key, out _);
        }

        return (false, default);
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var (found, value) = TryGet<T>(key);
        return Task.FromResult(found ? value : default);
    }

    public Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(expiration ?? RedisCacheService.DefaultExpiration);
        _storage[key] = new CacheEntry(value, expiresAt);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _storage.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefixKey, CancellationToken cancellationToken = default)
    {
        foreach (var key in _storage.Keys)
        {
            if (key.StartsWith(prefixKey, StringComparison.Ordinal))
            {
                _storage.TryRemove(key, out _);
            }
        }

        return Task.CompletedTask;
    }

    public async Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        Func<T, bool>? shouldCache = null,
        CancellationToken cancellationToken = default)
    {
        var (found, cached) = TryGet<T>(key);
        if (found)
        {
            return cached;
        }

        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            (found, cached) = TryGet<T>(key);
            if (found)
            {
                return cached;
            }

            var fresh = await factory(cancellationToken);
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
}
