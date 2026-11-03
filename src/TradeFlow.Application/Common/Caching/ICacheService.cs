namespace TradeFlow.Application.Common.Caching;

/// <summary>
/// Abstraction for distributed caching operations with single-flight stampede protection (ADR-0014).
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    Task RemoveByPrefixAsync(string prefixKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a cached item or invokes the factory delegate using single-flight mutex coordination
    /// to eliminate Cache Stampede (Dog-piling / Thundering Herd) against the primary datastore (ADR-0014).
    /// </summary>
    Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        Func<T, bool>? shouldCache = null,
        CancellationToken cancellationToken = default);
}
