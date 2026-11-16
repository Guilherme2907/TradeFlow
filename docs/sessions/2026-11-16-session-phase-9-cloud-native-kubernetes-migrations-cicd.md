# Session Checkpoint: Phase 9 — Cloud-Native Readiness, Kubernetes Operations, EF Core Migrations & CI/CD Pipeline

- **Date**: 2026-11-16
- **Project Stage**: Phase 9 — Cloud-Native Readiness, Production Operations, Health Probes, Graceful Shutdown, Multi-Schema EF Core Migrations & CI/CD Pipeline
- **Focus Area**: Kubernetes Architecture & Probes Isolation, Graceful Shutdown Drain, Multi-Schema EF Core Migrations, Multi-Stage Dockerfile, and GitHub Actions CI/CD (ADR-0017)

---

## 1. Implementation Progress

1. **Multi-Schema EF Core Migrations**:
   - Replaced runtime `DatabaseBootstrap.CreateTablesAsync()` with automated EF Core migrations.
   - Configured separate migration history tables per bounded context in [`DependencyInjection.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/DependencyInjection.cs): `orders."__EFMigrationsHistory"` and `inventory."__EFMigrationsHistory"`.
   - Created design-time factories in [`DesignTimeDbContextFactories.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Persistence/DesignTimeDbContextFactories.cs).
   - Generated initial schema migrations:
     - [`20261113213001_InitialOrdersSchema.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Modules/Orders/Persistence/Migrations/20261113213001_InitialOrdersSchema.cs)
     - [`20261113213006_InitialInventorySchema.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Modules/Inventory/Persistence/Migrations/20261113213006_InitialInventorySchema.cs)
   - Updated [`DatabaseBootstrap.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Api/Common/DatabaseBootstrap.cs) to run `MigrateAsync()` on startup while gracefully falling back to baseline schema verification for existing databases.

2. **Cloud-Native Health Checks & Graceful Shutdown**:
   - Configured decoupled probes in [`HealthChecksConfiguration.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Api/Common/Health/HealthChecksConfiguration.cs):
     - **Liveness** (`/health/live`): Self-check only (`Tags: ["live"]`). Evaluates strictly whether the .NET process is alive and responsive without deadlocks. Never probes external downstream dependencies.
     - **Readiness** (`/health/ready`): Evaluates PostgreSQL (critical dependency: failure yields HTTP 503 `Unhealthy`) and Redis (non-critical dependency: failure yields HTTP 200 `Degraded` to preserve order intake via fail-open cache-aside).
   - Configured graceful shutdown drain via `HostOptions.ShutdownTimeout = TimeSpan.FromSeconds(25)` in [`Program.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Api/Program.cs), paired with Kubernetes pod `terminationGracePeriodSeconds: 30`.

3. **Multi-Stage Containerization & Production Tooling**:
   - Built production multi-stage Alpine [`Dockerfile`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/Dockerfile) running as non-root `USER $APP_UID` (UID 1654), with layer caching for NuGet dependencies and invariant globalization.
   - Added [`.dockerignore`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/.dockerignore) to ensure small, reproducible build contexts.

4. **Kubernetes Manifests & Local Cluster Deployment**:
   - Created production manifest [`deploy/k8s/tradeflow-k8s.yaml`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/deploy/k8s/tradeflow-k8s.yaml) declaring ConfigMap, Deployment (3 replicas, RollingUpdate with `maxUnavailable: 0` and `maxSurge: 1`), Liveness/Readiness probes, ClusterIP Service, and HorizontalPodAutoscaler (HPA).
   - Created local manifest [`deploy/k8s/tradeflow-local.yaml`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/deploy/k8s/tradeflow-local.yaml) configured for Docker Desktop Kubernetes, exposing port `5022` via LoadBalancer.
   - Updated [`SecurityExtensions.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Api/Common/Security/SecurityExtensions.cs) with multi-issuer validation (`localhost:8080` and `host.docker.internal:8080`) to handle requests crossing container/host network boundaries.

5. **CI/CD Pipeline**:
   - Created 3-stage GitHub Actions workflow [`.github/workflows/ci-cd.yml`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/.github/workflows/ci-cd.yml):
     - Stage 1: Build & fast Unit/Architecture tests with `-warnaserror`.
     - Stage 2: Integration tests with real Docker Testcontainers (PostgreSQL, RabbitMQ, Kafka, Redis).
     - Stage 3: Container image build and vulnerability readiness.

---

## 2. Architectural Decisions Made

- **Accepted ADR-0017**: [Cloud-Native Readiness: Liveness/Readiness Health Probes, Graceful Shutdown, Multi-Schema EF Core Migrations, Multi-Stage Containerization & CI/CD Pipeline](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0017-cloud-native-readiness-health-probes-graceful-shutdown-migrations-cicd.md).
- **Decoupled Liveness from External Infra**: Proved why external dependencies must NEVER be checked in `/health/live`. If downstream databases or brokers hiccup, a liveness check failure causes kubelet to restart every application container simultaneously, transforming a transient downstream slow-down into a complete self-inflicted cascade outage.
- **Fail-Open Readiness for Non-Critical Infrastructure**: Redis outage reports `HealthStatus.Degraded` (HTTP 200 OK) because TradeFlow's Cache-Aside pattern safely falls back to PostgreSQL and in-memory caches without dropping orders.
- **Dedicated EF Core Migration History Tables per Schema**: Configured `orders."__EFMigrationsHistory"` and `inventory."__EFMigrationsHistory"`. Allows independent schema evolution without cross-context migration locks or schema contention.
- **Graceful Shutdown Duration Alignment**: K8s `terminationGracePeriodSeconds: 30` > App `ShutdownTimeout: 25s`. Guarantees that in-flight HTTP requests and transactional outbox batches complete cleanly before SIGKILL is dispatched.

---

## 3. Testing Decisions Made

- **Selected API & Cloud-Native Health Level**:
  - Implemented [`CloudNativeHealthCheckTests.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.Api.IntegrationTests/Health/CloudNativeHealthCheckTests.cs) via `WebApplicationFactory` and Testcontainers PostgreSQL.
  - Tests verify:
    1. Liveness probe returns 200 OK even when PostgreSQL is stopped/unreachable.
    2. Readiness probe returns 503 Service Unavailable when PostgreSQL is stopped.
    3. Readiness probe returns 200 OK when PostgreSQL is healthy.
    4. `HostOptions.ShutdownTimeout` is strictly set to 25 seconds.
- **Live Local Kubernetes Verification**:
  - Built `tradeflow-api:local` (255MB).
  - Deployed to Docker Desktop Kubernetes cluster across 3 active pods.
  - Interfaced with running Docker Compose containers (PostgreSQL, Redis, Keycloak, RabbitMQ, Kafka, Jaeger) using `host.docker.internal`.
  - Executed end-to-end tests:
    - Checked `/health/ready` -> returned HTTP 200 OK (`postgresql` Healthy in 2.32ms, `redis` Healthy in 1.12ms).
    - Issued JWT token via Keycloak.
    - Submitted Order via Kubernetes Service port 5022 -> saved in PostgreSQL, checked rate limit in Redis, captured traces in Jaeger.
    - Verified Scalar UI accessible at `http://localhost:5022/scalar/v1`.
- **Total Test Suite**: 141 automated tests (41 domain unit, 27 application unit, 19 architecture, 33 infrastructure integration, 21 API/health integration; 103 non-Docker passing in < 10 seconds).

---

## 4. Important Tests Added & What They Protect

| Test Name | File | Protected Behavior | Bug Prevention |
| :--- | :--- | :--- | :--- |
| `LivenessProbe_WhenDatabaseIsUnavailable_Returns200Ok` | [`CloudNativeHealthCheckTests.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.Api.IntegrationTests/Health/CloudNativeHealthCheckTests.cs) | Verifies process liveness check remains isolated from external dependency status. | Prevents cascading pod restart loops during transient database outages. |
| `ReadinessProbe_WhenDatabaseIsUnavailable_Returns503ServiceUnavailable` | [`CloudNativeHealthCheckTests.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.Api.IntegrationTests/Health/CloudNativeHealthCheckTests.cs) | Verifies K8s readiness probe fails fast when critical DB connection is broken. | Prevents K8s Service from routing traffic to pods that cannot persist transactions. |
| `ReadinessProbe_WhenDatabaseIsHealthy_Returns200Ok` | [`CloudNativeHealthCheckTests.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.Api.IntegrationTests/Health/CloudNativeHealthCheckTests.cs) | Verifies readiness endpoint returns 200 OK with detailed dependency latencies. | Prevents valid pods from being excluded from load balancer endpoints. |
| `GracefulShutdown_ConfiguresTimeoutTo25Seconds` | [`CloudNativeHealthCheckTests.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.Api.IntegrationTests/Health/CloudNativeHealthCheckTests.cs) | Verifies ASP.NET Core host shutdown timeout is registered as 25 seconds. | Prevents abrupt SIGKILL truncation of in-flight orders during rolling deployments. |

---

## 5. What Was Understood Well

- **Cascading Failure Mechanism**: Developer quickly recognized why external dependencies in liveness probes cause catastrophic cluster-wide crash loops.
- **Kubernetes DNS Service Discovery**: Understood how CoreDNS resolves internal services using `<service-name>.<namespace>.svc.cluster.local` and how that relates to ConfigMaps and Docker Compose host mappings (`host.docker.internal`).
- **RollingUpdate Deployment Mechanics**: Mastered how Kubernetes coordinates `maxUnavailable: 0` and `maxSurge: 1` alongside the Readiness probe to guarantee seamless, zero-downtime releases.
- **Developer vs. DevOps Boundary**: Understood which Kubernetes concerns belong to the application developer (probes, graceful shutdown, environment variable consumption, resource sizing) versus platform/DevOps engineers (Ingress controllers, DNS cert-managers, HPA tuning, network policies, node pools).

---

## 6. What Was Struggled With / Conceptual Gaps

- **Kubernetes Service Port Mapping vs Host Port Binding**: Initially experienced confusion when accessing `http://localhost:8080/scalar/v1` because Keycloak was already binding host port 8080. Resolved by moving the local Kubernetes LoadBalancer Service to port `5022` to isolate entrypoints.
- **Docker Desktop Kubernetes Context**: Initial `kubectl` connection failed with certificate mismatch because context was set to an Azure AKS cluster (`hmg-bot-aks-admin`). Resolved by switching context to `docker-desktop`.
- **JWT Issuer URL Mismatch**: Keycloak container reported token issuer based on incoming request host (`host.docker.internal` vs `localhost`). Resolved by configuring multi-issuer validation in `SecurityExtensions.cs`.

---

## 7. Important Technical Concepts Explored

- **Liveness vs. Readiness Probes**: Liveness answers *"Is the process alive or deadlocked?"*; Readiness answers *"Can the pod serve user traffic right now?"*.
- **Graceful Shutdown Lifecycle**: SIGTERM received -> K8s removes pod from Endpoints -> kubelet waits -> Application stops accepting new requests -> In-flight requests drain within 25s -> Clean exit.
- **Multi-Schema EF Core Migrations**: Using `.MigrationsHistoryTable(..., schema)` enables multiple independent bounded contexts within the same PostgreSQL instance to manage their schema lifecycles safely.

---

## 8. Intentionally Introduced Failures & Investigations

1. **Failure Injected**: Simulated database outage by pointing test client to invalid PostgreSQL connection string in `CloudNativeHealthCheckTests`.
   - **Observed Behavior**: `/health/live` returned HTTP 200 OK (`{"status":"Healthy"}`); `/health/ready` returned HTTP 503 Service Unavailable (`{"status":"Unhealthy", "entries":{"postgresql":{"status":"Unhealthy"}}}`).
   - **Resolution**: Confirmed probe isolation and correct HTTP status code contracts.

2. **Failure Injected**: Missing `RabbitMq` connection string in local standalone test manifest.
   - **Observed Behavior**: `RabbitMqConsumerBackgroundService` crashed on `localhost:5672` in-container, causing pod CrashLoopBackOff.
   - **Resolution**: Set `ConnectionStrings__RabbitMq` and other broker strings to `host.docker.internal` in local manifest.

---

## 9. Next Learning Step

- **Phase 10**: High-Concurrency Load Testing, Performance Profiling & Capacity Planning (k6 scripts, connection pool saturation, latency distribution analysis) or Capstone Architecture Defense & Final Wrap-up.
