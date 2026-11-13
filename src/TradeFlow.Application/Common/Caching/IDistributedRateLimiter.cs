namespace TradeFlow.Application.Common.Caching;

/// <summary>
/// Represents the decision of a distributed rate-limiting lease acquisition attempt (ADR-0016).
/// </summary>
public sealed record RateLimitLeaseResult(
    bool IsAllowed,
    long CurrentCount,
    int PermitLimit,
    TimeSpan RetryAfter);

/// <summary>
/// Abstraction for distributed rate limiting across multiple application replicas (pods),
/// supporting tenant-isolated and IP-based quotas with fail-open resilience (ADR-0016).
/// </summary>
public interface IDistributedRateLimiter
{
    Task<RateLimitLeaseResult> TryAcquireAsync(
        string partitionKey,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken = default);
}
