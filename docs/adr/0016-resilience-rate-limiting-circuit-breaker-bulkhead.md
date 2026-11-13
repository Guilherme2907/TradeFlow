# ADR-0016: Edge Rate Limiting, Load Shedding, and Outbound Resilience Pipelines with Polly v8 & Redis

- **Status**: Accepted
- **Date**: 2026-11-12
- **Deciders**: Architecture & Engineering Mentor / Developer
- **Phase**: Phase 8 — Advanced Resilience & Fault Tolerance

---

## 1. Context & Problem Statement

As TradeFlow prepares for high-concurrency B2B production workloads and horizontal multi-pod scaling, two systemic reliability vulnerabilities exist:

1. **Inbound Edge Saturation & Noisy Neighbor Vulnerability**:
   - Currently, API endpoints (`/api/orders/*` and `/api/auth/token`) have no rate limiting or concurrency load shedding.
   - A malfunctioning automated trading script from a single B2B customer tenant (`Noisy Neighbor`) can flood the API with thousands of requests per second, exhausting the PostgreSQL connection pool and starving legitimate tenants.
   - Relying solely on client IP addresses for rate limiting on B2B endpoints is an anti-pattern because hundreds of traders within a corporate enterprise often share a single outbound NAT/Proxy IP.
   - Furthermore, standard in-memory rate limiters maintain isolated counters per process; when scaling horizontally across $N$ Kubernetes pods, an attacker or buggy client effectively receives $N \times \text{Limit}$ capacity.

2. **Outbound Cascading Failure & Thread Pool Starvation**:
   - `POST /api/auth/token` exchanges credentials with Keycloak over HTTP using a default `HttpClient` (which has a 100-second default timeout and zero fault tolerance).
   - If Keycloak experiences latency spikes or partial outages, inbound login requests hang for up to 100 seconds, consuming server threads and sockets until the entire API suffers **Thread Pool Starvation** — taking down core order intake (`POST /api/orders`) even for traders who already possess valid JWTs.

---

## 2. Considered Options

### 2.1 Inbound Rate Limiting Partitioning & Storage
- **Option A: Global IP-Based In-Memory Rate Limiting**
  - *Pros*: Trivial single-line configuration.
  - *Cons*: Penalizes all traders behind a shared corporate NAT IP; does not coordinate limits across multiple API pods.
- **Option B: Context-Aware Partitioning (`tenant:{customer_id}` vs `ip:{client_ip}`) + Global Load Shedding + Redis Atomic Coordination with Fail-Open Fallback (Selected)**
  - *Pros*: Isolates B2B tenants from noisy neighbors; protects anonymous login endpoints against brute-force by IP; enforces global server concurrency limits (Load Shedding); coordinates counters across pods via Redis Lua scripts while degrading gracefully to in-memory tracking if Redis is unavailable.

### 2.2 Outbound HTTP Fault Tolerance
- **Option A: Manual `try/catch` Loops in Endpoints**
  - *Pros*: No library dependencies.
  - *Cons*: Lacks stateful Circuit Breaker coordination, jittered backoff, bulkhead isolation, and OpenTelemetry instrumentation.
- **Option B: Polly v8 Resilience Pipeline (`Microsoft.Extensions.Http.Resilience`) with Layered Strategy Ordering (Selected)**
  - *Pros*: First-class .NET `TimeProvider` support for deterministic testing, native OpenTelemetry metrics/tracing (`Polly` source), strict ordering (`TotalTimeout` $\rightarrow$ `Bulkhead` $\rightarrow$ `Retry` $\rightarrow$ `CircuitBreaker` $\rightarrow$ `AttemptTimeout`), and fail-fast `503 Service Unavailable` responses when the circuit opens.

---

## 3. Decision Outcome

We adopt **Option B** for both inbound edge protection and outbound HTTP resilience:

### 3.1 Inbound Protection Architecture
1. **Middleware Pipeline Ordering**:
   - `UseAuthentication()` $\rightarrow$ `UseRateLimiter()` $\rightarrow$ `UseAuthorization()`.
   - Placing `UseRateLimiter()` after `UseAuthentication()` ensures JWT claims (`customer_id`, `sub`) are already populated for tenant partitioning.
2. **Load Shedding (`ConcurrencyLimiter`)**:
   - Enforces a global ceiling on concurrent in-flight requests per pod. Requests exceeding the concurrency ceiling and queue limit are shed immediately in $< 1\,\text{ms}$ with `503 Service Unavailable` (`LOAD_SHEDDING_REJECTED`).
3. **Partitioned Rate Limiting Policies**:
   - **`TenantOrderPolicy` (`/api/orders/*`)**: Partitioned by `tenant:{customer_id}` (falling back to `user:{sub}` or `anon:{ip}`).
   - **`AuthBruteForcePolicy` (`/api/auth/token`)**: Partitioned by `ip:{remote_ip}` with stricter window quotas to mitigate credential stuffing.
   - **Distributed Redis Coordination**: When Redis is available, quota increments execute via an atomic Lua script (`INCR` + conditional `PEXPIRE`) so multi-pod deployments share a unified counter, failing open to local memory if Redis is unreachable.
   - Rejections return RFC 7807 `ProblemDetails` (`429 Too Many Requests`, code `RATE_LIMIT_EXCEEDED`) with a standard `Retry-After` HTTP header.

### 3.2 Outbound HTTP Resilience Pipeline (`Polly v8`) & Clean Architecture Encapsulation
We register each external dependency as an isolated Typed `HttpClient` (e.g., `AddHttpClient<IIdentityProviderService, KeycloakIdentityProviderService>().AddTradeFlowResiliencePipeline("Keycloak")`) backed by a 5-stage Polly v8 pipeline ordered from outermost to innermost:

```mermaid
flowchart LR
    Req["POST /api/auth/token"] --> T1["1. Total Timeout (10s)"]
    T1 --> BH["2. Bulkhead (Max 20 concurrent)"]
    BH --> RT["3. Retry (2x Exp Backoff + Jitter)"]
    RT --> CB["4. Circuit Breaker (50% fail / 15s break)"]
    CB --> T2["5. Attempt Timeout (3s)"]
    T2 --> KC["Keycloak OIDC"]
```

- **Multi-Service Isolation via Profile Name**: `AddTradeFlowResiliencePipeline(string pipelineName)` reads thresholds dynamically from `Resilience:{pipelineName}:*` in `IConfiguration` so each external dependency maintains an isolated Bulkhead and Circuit Breaker state machine.
- **Per-Instance Circuit Breaker vs. Distributed Rate Limiter**: While Rate Limiting represents a business/tenant quota that must be coordinated across pods in Redis, Circuit Breaker state intentionally resides in-memory per pod to handle partial AZ/network partitions, converge naturally within seconds on full outages, and execute fail-fast rejections in nanoseconds without network I/O.
- **Transient-Only Predicate**: Retries and Circuit Breaker only trigger on transient faults (`5xx`, `408 Request Timeout`, `429 Too Many Requests`, `HttpRequestException`, and `TimeoutRejectedException`). Deterministic client errors (`400 Bad Request`, `401 Unauthorized`, `403 Forbidden`) **never** trigger retries or trip the circuit breaker.
- **Centralized Resilience-to-Result Translation (`ResilienceHttpExecutor`)**: Outbound HTTP calls in `TradeFlow.Infrastructure` execute through `httpClient.SendWithResilienceAsync(...)`, which centrally catches `BrokenCircuitException`, `TimeoutRejectedException`, `RateLimiterRejectedException`, and `HttpRequestException`, enriches the active OpenTelemetry span via `Activity.Current.RecordError(ex)`, and returns `Result.Failure<HttpResponseMessage>(Error.Unavailable(...))`. This keeps API endpoints (`AuthEndpoints.cs`) completely free of `HttpClient` transport logic and `try/catch` boilerplate, mapping `ErrorType.Unavailable` to RFC 7807 `503 Service Unavailable` with a `Retry-After: 15` header via `ResultExtensions.ToHttpResult()`.

---

## 4. Consequences

### Positive
- **Noisy Neighbor Immunity**: A runaway script in Tenant Alpha returns `429 Too Many Requests` for Tenant Alpha while Tenant Beta continues trading with zero latency impact.
- **Thread Pool Survival (Bulkhead + Circuit Breaker)**: Even if Keycloak hangs completely, at most 20 concurrent calls are permitted, each timing out in 3s, after which the Circuit Breaker opens and fails subsequent logins in $< 1\,\text{ms}$ — keeping `/api/orders` 100% healthy.
- **Deterministic Testability**: Injecting `FakeTimeProvider` allows automated tests to verify the entire Circuit Breaker lifecycle (`Closed` $\rightarrow$ `Open` $\rightarrow$ `Half-Open` $\rightarrow$ `Closed`) in milliseconds without `Thread.Sleep`.

### Negative / Trade-offs
- Slightly higher complexity in `HttpClient` registration and configuration tuning (`ResilienceOptions`).
- Rate-limiting Redis checks add $\sim 0.3\,\text{ms}$ of local network overhead per request when Redis is active (mitigated by pipelined single-roundtrip Lua execution and Fail-Open fallback).
