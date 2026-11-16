# ADR-0017: Cloud-Native Readiness, Kubernetes Health Probes, Graceful Shutdown, Multi-Schema EF Migrations & CI/CD

- **Status**: Accepted
- **Date**: 2026-11-13
- **Deciders**: Architecture & Engineering Mentor / Developer
- **Phase**: Phase 9 — Cloud-Native Readiness, Production Operations & CI/CD

---

## 1. Context & Problem Statement

With Phases 1 through 8 complete, TradeFlow has a resilient, observable, and event-driven Modular Monolith architecture. However, running in a Cloud-Native container orchestrator (such as Kubernetes) and automating continuous delivery requires resolving four operational challenges:

1. **Liveness vs. Readiness Probe Separation**:
   - If Kubernetes uses a single `/health` endpoint that checks external dependencies (PostgreSQL, Redis, RabbitMQ) for both Liveness and Readiness, a transient 15-second database or broker hiccup will cause Kubernetes to kill and restart all API pods simultaneously (cascading restart storm).
   - Furthermore, because Redis caching (ADR-0014) and rate limiting (ADR-0016) are designed as **Fail-Open**, a Redis outage should mark the pod as `Degraded` (keeping HTTP `200 OK` so the pod stays in the Load Balancer while alerting SRE dashboards) rather than `Unhealthy` (`503 Service Unavailable`).
2. **Graceful Shutdown (`SIGTERM`) & In-Flight Drain**:
   - During Kubernetes rolling updates or scale-down events, pods receive `SIGTERM`. Abrupt termination risks dropping in-flight HTTP transactions, leaving Outbox batches half-acknowledged, or triggering slow 45-second Kafka consumer group rebalances due to missing `consumer.Close()` calls.
3. **Production Database Schema Evolution (`CreateTablesAsync` vs. Multi-Schema EF Core Migrations)**:
   - `RelationalDatabaseCreator.CreateTablesAsync()` cannot evolve existing production tables with live customer data.
   - Because TradeFlow isolates modules into separate PostgreSQL schemas (`orders` and `inventory`) with dedicated `DbContext`s (`OrdersDbContext` and `InventoryDbContext`), each module requires an isolated migration history table (`orders.__EFMigrationsHistory` and `inventory.__EFMigrationsHistory`).
4. **Container Security & Automated CI/CD Verification**:
   - Production container images must exclude the .NET SDK, minimize image footprint via Multi-Stage builds, and run under a non-root user (`$APP_UID`).
   - Every commit/PR must automatically verify compilation, architectural fitness rules (`NetArchTest`), unit tests, and real Docker Testcontainers integration suites before building production artifacts.

---

## 2. Considered Options

### 2.1 Health Check Topology
- **Option A: Single `/health` Endpoint Checking All Dependencies**
  - *Cons*: Couples Liveness to external dependencies, causing pod restart storms during transient infrastructure blips.
- **Option B: Split `/health/live` (Self-Only) and `/health/ready` (PostgreSQL Critical / Redis Fail-Open Degraded) (Selected)**
  - *Pros*: `/health/live` only verifies that the .NET Kestrel process is responsive; `/health/ready` verifies PostgreSQL (`Unhealthy` / `503` if down, removing the pod from the Service load balancer without restarting it) and Redis (`Degraded` / `200` if down, preserving traffic flow in Fail-Open mode).

### 2.2 Database Migration Strategy
- **Option A: Keep `CreateTablesAsync()` + Manual Ad-Hoc `ALTER TABLE` Scripts**
  - *Cons*: Fragile, unversioned, and prone to schema drift across environments.
- **Option B: Per-Module EF Core Migrations with Schema-Scoped `__EFMigrationsHistory` (Selected)**
  - *Pros*: `OrdersDbContext` tracks migrations in `orders.__EFMigrationsHistory` and `InventoryDbContext` tracks migrations in `inventory.__EFMigrationsHistory`. Preserves module autonomy for future microservice extraction and supports CI/CD migration bundles (`dotnet ef migrations bundle`).

---

## 3. Decision Outcome

We adopt **Option B** across all Cloud-Native operational axes:

1. **Kubernetes Health Probes (`HealthChecksConfiguration.cs`)**:
   - `GET /health/live`: Executes only the `"self"` liveness check (`tags: ["live"]`).
   - `GET /health/ready`: Executes dependency checks (`tags: ["ready"]`):
     - `postgresql`: Verifies `OrdersDbContext` and `InventoryDbContext` connectivity (`Unhealthy` $\rightarrow$ HTTP `503` on failure).
     - `redis`: Verifies `IConnectionMultiplexer` connectivity when configured (`Degraded` $\rightarrow$ HTTP `200` with `"status": "Degraded"` on failure, reflecting Fail-Open resilience).
2. **Graceful Shutdown (`HostOptions.ShutdownTimeout = 25s`)**:
   - Configured in `Program.cs` so Kestrel stops accepting new requests on `SIGTERM` and grants in-flight HTTP requests and `BackgroundService` workers (`OutboxPublisher`, `RabbitMqConsumer`, `KafkaConsumer`) up to 25 seconds to drain cleanly before Kubernetes' 30-second `terminationGracePeriodSeconds` expires.
3. **Per-Schema EF Core Migrations**:
   - Configured `.MigrationsHistoryTable("__EFMigrationsHistory", "orders")` and `.MigrationsHistoryTable("__EFMigrationsHistory", "inventory")`.
   - Generated initial schema migrations under `Modules/Orders/Persistence/Migrations` and `Modules/Inventory/Persistence/Migrations`.
4. **Multi-Stage `Dockerfile`, Kubernetes Manifests (`deploy/k8s/`), and GitHub Actions CI/CD (`.github/workflows/ci-cd.yml`)**:
   - Multi-stage Alpine build (`sdk:10.0-alpine` $\rightarrow$ `aspnet:10.0-alpine`) running as non-root `$APP_UID`.
   - Kubernetes `Deployment`, `Service`, `ConfigMap`, and `HorizontalPodAutoscaler` manifests wired to `/health/live` and `/health/ready`.
   - Automated GitHub Actions pipeline executing fast unit/architecture tests, Testcontainers integration suites, and Docker image build verification.

---

## 4. Consequences

### Positive
- **Zero-Downtime Rolling Updates & Immunity to Restart Storms**: Pods are never restarted due to downstream database or broker blips, and in-flight work drains cleanly on `SIGTERM`.
- **Schema Evolution Autonomy**: Each bounded context owns its migration lifecycle and history table inside its own PostgreSQL schema.
- **Production-Grade Container Security**: Final runtime image contains no compilers/SDK and runs with least privilege (`$APP_UID`).

### Negative / Trade-offs
- Schema changes must follow **Expand-and-Contract (Backward-Compatible)** discipline during rolling updates so `v1.0` and `v1.1` pods can coexist briefly against the same database schema.
