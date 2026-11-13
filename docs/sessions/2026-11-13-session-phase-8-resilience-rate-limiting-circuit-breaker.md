# Session Log — 2026-11-13: Phase 8 Advanced Resilience, Fault Tolerance & Edge Protection

- **Date**: 2026-11-13
- **Stage**: Phase 8 — Advanced Resilience, Fault Tolerance & Edge Protection (Polly v8 & Redis Lua)
- **Status**: Completed (`ADR-0016` Accepted, 137/137 Automated Tests Passing)

---

## 1. Session Objectives & Context

1. Complete live manual testing and observability triage of Phases 1–7 in Jaeger UI, fixing two subtle runtime integration bugs discovered during manual verification:
   - Redis deserialization of `Result<OrderResponse>` (`ResultJsonConverterFactory`).
   - OpenTelemetry exception event enrichment (`Activity.RecordError`) across `UseExceptionHandler` and background RabbitMQ/Kafka consumers.
2. Conduct Socratic consolidation across Phases 1–7 (`SELECT ... FOR UPDATE` + `LockRows` execution internals, composite Inbox key `(message_id, consumer_name)`, Kafka `ProductId` partitioning, Redis `tenant:{customerId}` cache key isolation, and W3C `traceparent` Outbox persistence).
3. Design, implement, critique, and verify **Phase 8: Advanced Resilience & Fault Tolerance (`ADR-0016`)**:
   - Inbound Global Load Shedding (`SemaphoreSlim.Wait(0)`) and Distributed Multi-Tenant Rate Limiting (`RedisDistributedRateLimiter` atomic Lua script + `InMemoryDistributedRateLimiter` Fail-Open fallback).
   - Outbound 5-stage Polly v8 HTTP Resilience Pipeline (`TotalTimeout` $\rightarrow$ `Bulkhead` $\rightarrow$ `Retry` $\rightarrow$ `CircuitBreaker` $\rightarrow$ `AttemptTimeout`).
   - Clean Architecture encapsulation via `IIdentityProviderService`, `KeycloakIdentityProviderService`, and centralized `ResilienceHttpExecutor`.

---

## 2. Key Architectural Discussions & Breakthroughs

1. **PostgreSQL `SELECT ... FOR UPDATE` Internals (`Sort` $\rightarrow$ `LockRows`)**:
   - Clarified why `SELECT ... FOR UPDATE` does not acquire locks atomically as a single batch, but tuple-by-tuple as rows stream into the `LockRows` execution node. Therefore, SQL-level `.OrderBy(i => i.ProductId)` before `.ToListAsync()` is mandatory to guarantee deterministic lock acquisition order across concurrent transactions.
2. **Multi-Tenant Rate Limiting vs. Corporate NAT IP & Multi-Pod Coordination**:
   - The developer independently identified that IP-only rate limiting allows a single malicious or noisy trader behind a shared corporate IP to block all other users, proposing authenticated partitioning by user/tenant ID and anonymous partitioning by IP, backed by Redis for multi-instance coordination.
   - Analyzed the classic Redis `INCR` + `EXPIRE` race condition ("immortal key" bug if a pod crashes between commands) and solved it with a single-roundtrip atomic Lua script (`INCR` + conditional `PEXPIRE` + `PTTL`).
3. **Polly v8 5-Stage Pipeline Ordering & Loop Mechanics**:
   - Traced the exact execution flow from outermost to innermost (`TotalTimeout` $\rightarrow$ `Bulkhead` $\rightarrow$ `Retry` $\rightarrow$ `CircuitBreaker` $\rightarrow$ `AttemptTimeout`), confirming how retries loop inside the held Bulkhead slot and how `TotalTimeout` aborts in-flight retries with `TimeoutRejectedException`.
4. **Code Review Critique — Eliminating `try/catch` Proliferation in Endpoints**:
   - During code review, the developer critiqued having Polly/HTTP `try/catch` blocks inside `AuthEndpoints.cs`.
   - Refactored outbound HTTP calls into `IIdentityProviderService` (`KeycloakIdentityProviderService`) backed by a reusable `ResilienceHttpExecutor.SendWithResilienceAsync(...)` helper in `TradeFlow.Infrastructure` and `ErrorType.Unavailable` (`503 Service Unavailable` + `Retry-After: 15`) in `ResultExtensions.cs`.
5. **Per-Instance Circuit Breaker State vs. Distributed Rate Limiter State**:
   - Discussed why Circuit Breaker state intentionally lives in-memory per pod (protecting against partial AZ/network partitions, converging naturally on full outages, and failing fast in nanoseconds without network I/O) whereas Rate Limiting enforces a contractual tenant quota that must be coordinated globally in Redis.

---

## 3. Testing Decisions & Verification

- Added `RedisDistributedRateLimiter_AcrossMultiplePodInstances_ShouldCoordinateAtomicCounterInRedis` in `RedisCacheIntegrationTests.cs`.
- Created `ResilienceAndRateLimitingTests.cs` using `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) to deterministically verify:
  - `401 Unauthorized` never triggers retries (`attemptCount == 1`).
  - Transient `503 Service Unavailable` retries and recovers (`attemptCount == 3`).
  - Full Circuit Breaker state machine (`Closed` $\rightarrow$ `Open` throwing `BrokenCircuitException` $\rightarrow$ `FakeTimeProvider.Advance(16s)` $\rightarrow$ `Half-Open` $\rightarrow$ `Closed`) in $< 10\,\text{ms}$.
  - `InMemoryDistributedRateLimiter` window expiration via `FakeTimeProvider`.
  - Multi-tenant Noisy Neighbor isolation (`Tenant Alpha` throttled with `429 RATE_LIMIT_EXCEEDED` while `Tenant Beta` succeeds with `201 Created`).
  - `/api/auth/token` fail-fast `503 IDENTITY_PROVIDER_UNAVAILABLE` with `Retry-After` header when Keycloak is down.

---

## 4. Next Steps (Phase 9)

- **Phase 9 — Cloud-Native Readiness, Production Operations, Health Checks & CI/CD**:
  - Deep Liveness vs. Readiness vs. Startup Health Checks (`/health/live`, `/health/ready`) verifying PostgreSQL, Redis, RabbitMQ, and Kafka connectivity.
  - Graceful Shutdown (`IHostedService` drain, in-flight request completion, Kubernetes `SIGTERM` lifecycle).
  - Production multi-stage `Dockerfile`, EF Core Migrations strategy, and Kubernetes / CI/CD readiness.
