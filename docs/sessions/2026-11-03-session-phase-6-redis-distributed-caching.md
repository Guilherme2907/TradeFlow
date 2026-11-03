# Study Session Checkpoint — Phase 6: Distributed Caching & High-Performance Resiliency (Redis 7)

- **Date**: 2026-11-03
- **Stage**: Phase 6 Completed (100% green across all 124 tests including Docker Testcontainers)
- **Decisions Recorded**:
  - [ADR-0014: Distributed Caching (Cache-Aside), Single-Flight Stampede Protection & MediatR Pipeline Behavior](../adr/0014-distributed-caching-cache-aside-stampede-protection-and-pipeline-behavior.md)
- **Commits**:
  - `f1e6431`: `feat(caching): implement Redis distributed caching, single-flight stampede protection, and MediatR caching behavior`

---

## 1. Executive Summary & Breakthroughs

During this session, TradeFlow implemented **Phase 6: Distributed Caching & Resiliency** with Redis 7:

1. **Cache-Aside Pattern via Clean Architecture MediatR Pipeline**:
   - Query handlers (`GetOrderByIdQueryHandler`) remain 100% pure and decoupled from caching technology and `StackExchange.Redis`.
   - Any query implementing `ICacheableQuery` (`CacheKey`, `Expiration`) is intercepted transparently by `CachingBehavior<TRequest, TResponse>`.
   - Suppresses negative response caching (`Result { IsFailure: true }`) so ephemeral 404s, 403s, or validation errors are never cached long-term.

2. **Cache Stampede (Dog-piling / Thundering Herd) Elimination**:
   - Articulated the severe production risk of 500+ concurrent cache misses slamming PostgreSQL simultaneously and exhausting the database connection pool.
   - Designed single-flight double-checked locking mutex in `ICacheService.GetOrCreateAsync` using keyed async `SemaphoreSlim`.
   - The first request acquires the lock and queries PostgreSQL; all concurrent requests await and return the freshly cached value without touching the database.
   - Added randomized TTL Jitter (5%–10% variance) to prevent synchronized mass expiration of keys.

3. **Tenant-Scoped Cache Isolation (BOLA / IDOR Protection)**:
   - Evaluated the security vulnerability where global key `orders:{id}` would allow Tenant B to read Tenant A's cached order, bypassing authorization handlers.
   - Enforced tenant partition keys: `$"orders:{OrderId}:tenant:{TenantCustomerId}"`.

4. **Fail-Open Operational Resiliency**:
   - In `RedisCacheService`, all Redis operations (`StringGetAsync`, `StringSetAsync`, `KeyDeleteAsync`) are wrapped in try/catch blocks logging warnings.
   - If Redis crashes, experiences network partitions, or is unreachable, the system fails open gracefully and executes the underlying database factory, ensuring the API stays online.

5. **Read Query & Endpoint Exposed**:
   - Implemented `GetOrderByIdQuery` and `GET /api/orders/{id}` protected by `TradeFlowPolicies.OrderReadPolicy`.
   - Enforced BOLA/IDOR authorization: `Trader` can only access orders matching their signed `customer_id` claim; `Admin` and `RiskManager` can read cross-tenant orders.

---

## 2. Test Suite Status

Total Automated Tests: **124 tests**
- **Domain Unit Tests**: 41 tests (Aggregates, Value Objects, temporal audit tracking)
- **Application Unit Tests**: 27 tests (Command & Query handlers, `CachingBehavior` cache-hit/cache-miss/factory execution, validation behaviors)
- **Architecture Tests**: 19 tests (`NetArchTest` fitness rules, query sealing and naming rules, layer isolation, pure application layer)
- **API Security Integration Tests**: 11 tests (Includes `GetOrderById` positive read and negative cross-tenant BOLA/IDOR 403 checks)
- **Infrastructure Integration Tests**: 26 tests (Includes `RedisCacheIntegrationTests` with 50 concurrent requests stampede proof, get/set, and prefix eviction)

*Execution Result*: **124 of 124 tests passing (100% green)** across all 5 test projects including real PostgreSQL 16, RabbitMQ 4, Kafka KRaft, and Redis 7 in Testcontainers.

---

## 3. Real Failure Uncovered & Resolved during Integration Testing

- **Failure Discovered**: In `RedisCacheIntegrationTests`, testing single-flight stampede protection with `decimal` values (`test:stampede:quote:copper`) failed with all 50 concurrent requests returning `0M` instead of executing the factory.
- **Root Cause**: In C# generic methods, unconstrained `T?` with value types (like `decimal` or structs) evaluates `default(T)` to `0M`. In `GetOrCreateAsync`, the check `if (cached is not null)` was evaluated on `(decimal)0M`. Since value types are never null in .NET, `0M is not null` evaluated to `true`, treating a cache miss as a valid cached value of zero!
- **Staff-Level Engineering Fix**: Refactored `GetOrCreateAsync` in both `RedisCacheService` and `InMemoryCacheService` to use an explicit `(bool Found, T? Value)` pattern (`TryGetAsync`). Only when `Found == true` is the cached value returned; on cache miss (`Found == false`), the single-flight double-checked lock proceeds to execute the database factory.

---

## 4. Next Steps & Road Ahead

1. Transition to **Phase 7: Distributed Tracing & Observability with OpenTelemetry (HTTP, PostgreSQL, RabbitMQ, Kafka, Redis)**.
2. Instrument end-to-end W3C TraceContext (`traceparent` header) propagation across synchronous HTTP calls and asynchronous messaging/streaming boundaries.
