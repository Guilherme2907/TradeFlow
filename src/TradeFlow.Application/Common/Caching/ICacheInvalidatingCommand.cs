namespace TradeFlow.Application.Common.Caching;

/// <summary>
/// Marker interface for commands that mutate state cached by <see cref="ICacheableQuery"/>
/// and must evict matching cache key prefixes upon successful execution (ADR-0014).
/// </summary>
public interface ICacheInvalidatingCommand
{
    IReadOnlyCollection<string> CachePrefixesToInvalidate { get; }
}
