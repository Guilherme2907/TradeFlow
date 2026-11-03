namespace TradeFlow.Application.Common.Caching;

/// <summary>
/// Marker interface for queries that should be transparently cached via <see cref="Behaviors.CachingBehavior{TRequest, TResponse}"/> (ADR-0014).
/// </summary>
public interface ICacheableQuery
{
    /// <summary>
    /// Unique cache key identifier (e.g., <c>"orders:12345"</c>).
    /// </summary>
    string CacheKey { get; }

    /// <summary>
    /// Optional time-to-live. Defaults to standard policy (e.g., 5 minutes) when null.
    /// </summary>
    TimeSpan? Expiration { get; }
}
