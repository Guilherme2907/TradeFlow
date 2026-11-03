# ADR-0014: Distributed Caching (Cache-Aside), Single-Flight Stampede Protection & MediatR Pipeline Behavior

- **Status**: Accepted
- **Date**: 2026-11-03
- **Deciders**: Architecture & Engineering Mentor / Developer
- **Phase**: Phase 6 — Distributed Caching & High-Performance Resiliency

---

## 1. Context & Problem Statement

In TradeFlow B2B trading operations, read operations significantly outnumber write operations (read-to-write ratio typically ranges from $10:1$ to $100:1$). High-frequency reads include product catalog lookups, available inventory quantities, and order detail inspections (`GET /api/orders/{id}`).

Serving high-frequency reads directly from PostgreSQL introduces three critical systemic bottlenecks:
1. **Connection Pool Starvation**: High concurrency quickly exhausts database connection pools, causing connection timeouts for write-critical transactions.
2. **CPU & I/O Saturation**: Repetitive queries for relatively static data waste database computing capacity.
3. **High Latency**: Relational query planning and network round-trips to disk-backed databases typically incur $5\text{--}50\,\text{ms}$, compared to sub-millisecond memory lookups ($<1\,\text{ms}$).

### The Cache Stampede (Dog-Piling / Thundering Herd) Risk
When a hot cache key expires under heavy traffic (e.g., $1{,}000$ concurrent requests per second), all concurrent requests experience a simultaneous **Cache Miss**. If implemented naively, all $1{,}000$ requests execute the expensive database query simultaneously. This spikes database CPU to $100\%$, starvates connection pools, and can cause a cascading outage (*Thundering Herd Failure*).

### Clean Architecture & Maintainability Risk
Embedding cache lookup and storage logic directly inside every MediatR query handler violates the **Single Responsibility Principle (SRP)**, introduces repetitive boilerplate code, clutters unit tests with cache mocking, and tightly couples query logic to caching infrastructure.

---

## 2. Decision

We implement a **Resilient Distributed Caching Architecture** powered by **Redis 7** utilizing the **Cache-Aside Pattern**, **Single-Flight Stampede Protection**, and **MediatR Pipeline Behavior Decoupling**:

### 2.1 Clean Architecture Decoupling via `ICacheableQuery` & `CachingBehavior`
1. **Marker Interface**: Queries that opt into caching implement [`ICacheableQuery`](../../src/TradeFlow.Application/Common/Caching/ICacheableQuery.cs):
   - `string CacheKey { get; }`
   - `TimeSpan? Expiration { get; }`
2. **MediatR Pipeline Interceptor**: [`CachingBehavior<TRequest, TResponse>`](../../src/TradeFlow.Application/Common/Behaviors/CachingBehavior.cs) intercepts queries before the handler:
   - If the key exists in cache (**Cache Hit**), the cached response is deserialized and returned immediately without invoking the handler or hitting the database.
   - If missing (**Cache Miss**), the inner pipeline/handler is executed, the successful response is stored in Redis, and then returned.
   - **Failure Isolation**: If the handler returns a failure result (`Result { IsFailure: true }`), the result is **not cached**, preventing the proliferation of negative/error states.
3. **Pure Query Handlers**: Query handlers (such as `GetOrderByIdQueryHandler`) remain $100\%$ focused on database queries and DTO mapping, free of cache dependencies.

### 2.2 Single-Flight Stampede Protection (Keyed Mutex / Semaphore)
To eliminate the *Thundering Herd* problem, [`ICacheService.GetOrCreateAsync`](../../src/TradeFlow.Application/Common/Caching/ICacheService.cs) coordinates concurrent cache misses for the exact same key:
- When a cache miss occurs, only **one single execution** acquires a lock/semaphore for that specific cache key to fetch data from the primary store.
- Concurrent requests for that same key await the single-flight operation asynchronously.
- Once the first execution populates Redis, waiting requests wake up and read the fresh value directly from cache.
- Database load on cache expiration drops from $N$ simultaneous queries to exactly **1 query**.

### 2.3 TTL Jitter (Randomized Expiration Skew)
To prevent simultaneous expiration of multiple cache keys populated around the same time, cache durations incorporate random jitter:
$$\text{ActualTTL} = \text{BaseTTL} + \text{RandomJitter}(5\%\text{--}10\%)$$

### 2.4 Fail-Open Resilience (Graceful Degradation)
If the Redis broker becomes unavailable or network connectivity fails:
- The cache service logs the error and gracefully falls back to executing the underlying query directly against PostgreSQL.
- Distributed cache outages must **never** take down the API or throw $500$ Internal Server Errors to clients (*Fail-Open Design*).

---

## 3. Consequences

### Positive
- **Sub-Millisecond Read Latency**: Cache hits return in $<1\,\text{ms}$, offloading up to $95\%$ of read traffic from PostgreSQL.
- **Immunity to Thundering Herd**: Single-flight keyed locks protect PostgreSQL connection pools from stampedes upon key expiration.
- **Pure Clean Architecture**: Handlers and domain logic remain agnostic of Redis. Caching is a pluggable cross-cutting concern.
- **Fail-Open Operational Safety**: Temporary Redis downtime causes degraded read latency, but zero downtime or application crashes.

### Trade-offs & Mitigations
- **Eventual Consistency Window**: Data mutations on an entity may take up to the key's TTL to reflect in reads unless explicitly invalidated.
  - *Mitigation*: Key mutations emit domain events or command handlers perform targeted eviction via `ICacheService.RemoveAsync(key)`.
- **Serialization Overhead**: Complex object graphs require JSON serialization/deserialization.
  - *Mitigation*: Using optimized `System.Text.Json` with `JsonSerializerDefaults.Web`.
