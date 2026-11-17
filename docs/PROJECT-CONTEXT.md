# Project Context — TradeFlow

This document captures the real-time architectural baseline, active modules, technological footprint, testing baseline, and technical debt of the repository. It is continuously updated across sessions.

---

## 1. Project Status Overview

| Attribute | Current Value |
| :--- | :--- |
| **Current Stage** | `PHASE 8: Advanced Resilience, Fault Tolerance & Edge Protection (Polly v8 & Redis Lua) [ACTIVE]` |
| **Application Status** | Phase 1 to 8 Complete; Edge Load Shedding, Multi-Tenant Distributed Rate Limiting & Polly v8 Outbound Pipelines |
| **Active Architecture**| Modular Monolith (Clean Architecture + PostgreSQL Schemas + Outbox + RabbitMQ + Kafka KRaft + Keycloak OIDC + Redis Caching & Rate Limiting + OpenTelemetry + Polly v8 Resilience) |
| **Primary Codebase** | .NET 10, C# |
| **Active Testing Baseline** | Pure Domain Unit (41), Application Unit (27), Architecture (19), Infrastructure Integration Testcontainers (33), API Integration & Resilience (17) — 137 total automated tests |
| **Repository Root** | `g:\Estudo\FullStack-Engineer-Plan\TradeFlow` |
| **Last Updated** | 2026-11-13 |

> [!NOTE]
> Phase 8 (Advanced Resilience, Fault Tolerance & Edge Protection) is complete. Inbound traffic is protected by `TradeFlowResilienceMiddleware` enforcing global Load Shedding (`SemaphoreSlim.Wait(0)`) and context-aware Distributed Rate Limiting (`tenant:{customer_id}` for B2B order routes vs `auth-ip:{ip}` for brute-force protection) backed by atomic Redis Lua scripts (`INCR` + `PEXPIRE` + `PTTL`) with automatic Fail-Open fallback to `InMemoryDistributedRateLimiter`. Outbound HTTP dependencies (`KeycloakIdentityProviderService`) are isolated via Typed `HttpClient` profiles (`AddTradeFlowResiliencePipeline`) with a 5-stage Polly v8 pipeline (`TotalTimeout` -> `Bulkhead` -> `Retry` -> `CircuitBreaker` -> `AttemptTimeout`) and centralized `ResilienceHttpExecutor` mapping transient faults to `ErrorType.Unavailable` (`503 Service Unavailable`). All 137 automated tests passing with 0 warnings.

---

## 2. Architectural & Testing Footprint

### 2.1 Current Architecture Style
- **Status**: Modular Monolith with Clean Architecture (`TradeFlow.Domain`, `TradeFlow.Application`, `TradeFlow.Infrastructure`, `TradeFlow.Api`).
- **Enforcement**:
  - Relational schema partitioning in PostgreSQL: `orders` schema and `inventory` schema.
  - In-process module communication strictly through explicit contracts (`IInventoryModule`), prohibiting cross-context internal domain entity references.
  - Automated architecture fitness tests via `NetArchTest.Rules`.
  - Base `AggregateRoot<TId>` encapsulates temporal audit tracking (`CreatedAt`, `UpdatedAt`, `Touch()`) and optimistic concurrency tokens (`Version`).
  - High-contention inventory allocation utilizes pessimistic row-level locking (`FOR UPDATE`) with deterministic `ProductId` ordering to prevent deadlocks and eliminate OCC retry storms (ADR-0009).

### 2.2 Active Modules / Bounded Contexts
- `Orders` (Order aggregate root, OrderLine entity, order state transitions, Minimal APIs).
- `Inventory` (InventoryItem aggregate root, physical vs. allocated vs. available stock, atomic multi-item stock reservation facade `IInventoryModule`, pessimistic row locking with deterministic sorting, OCC rowversion concurrency token).
- `Catalog` (Product identifier reference `ProductId` in Domain.Common, price snapshots on order lines).

### 2.3 Active Storage & Persistence
- PostgreSQL for relational persistence via EF Core (`Npgsql.EntityFrameworkCore.PostgreSQL`).
  - Dedicated `DbContext` per bounded context: `OrdersDbContext` (`orders` schema) and `InventoryDbContext` (`inventory` schema).
  - All integration tests execute against real PostgreSQL via Testcontainers (`postgres:16-alpine`), eliminating in-memory emulators.

### 2.4 Active Messaging & Integration
- Asynchronous messaging (Phase 3 Completed):
  - Transactional Outbox pattern implemented via EF Core `DomainEventsToOutboxMessagesInterceptor`, `orders.outbox_messages` table, `OutboxProcessor` (`FOR UPDATE SKIP LOCKED`), and `OutboxPublisherBackgroundService` (ADR-0011).
  - RabbitMQ broker integration (`RabbitMQ.Client` v7.2.2) via `RabbitMqConnectionManager` (Singleton TCP connection), `RabbitMqOutboxMessagePublisher` (Scoped channel with Publisher Confirms), and `RabbitMqTopology` (`tradeflow.events` Topic Exchange + Dead-Letter Exchange `tradeflow.events.dlx`).
  - Push-based consumer workers (`RabbitMqConsumerBackgroundService` with `BasicQosAsync(prefetchCount: 10)` backpressure) routing events to `OrderSubmittedInvoiceConsumer`, `OrderCancelledNotificationConsumer`, and `OrderLifecycleAuditConsumer`.
  - Idempotent consumer deduplication via `IdempotentInboxConsumer` into `orders.inbox_messages` (`INSERT ... ON CONFLICT DO NOTHING`).
- High-throughput event streaming (Phase 4 Active):
  - Apache Kafka integration (`Confluent.Kafka` v2.16.0) via `KafkaTopology` (`tradeflow.market.trades.v1` with 3 partitions + `tradeflow.market.trades.dlq`), `KafkaTradeStreamProducer` (Singleton thread-safe producer with `Acks.All`, `EnableIdempotence = true`, and `ProductId` Partition Key), `KafkaTradeStreamConsumerBackgroundService` (manual offset commit after processing + non-blocking Poison Pill routing to DLT), and `MarketTradeAnalyticsHandler` (ADR-0012).

### 2.5 Active Observability, Tracing & Resilience (Phases 7 & 8 Active)
- OpenTelemetry .NET SDK with `TradeFlowDiagnostics` (`ActivitySource("TradeFlow")`) and `Polly` activity source.
- Automatic instrumentations for ASP.NET Core, EF Core, Npgsql PostgreSQL, and StackExchange Redis, plus custom exception event enrichment (`RecordError`).
- W3C TraceContext propagation across persistence barrier (`orders.outbox_messages.trace_parent` and `trace_state`).
- AMQP byte-array header injection/extraction for RabbitMQ and UTF-8 string header injection/extraction for Kafka.
- OTLP Exporter over gRPC (`http://localhost:4317`) sending spans to Jaeger all-in-one container (`COLLECTOR_OTLP_ENABLED=true`), with Jaeger UI on `:16686` (ADR-0015).
- Inbound `TradeFlowResilienceMiddleware` (Global Load Shedding + Distributed Multi-Tenant Rate Limiting via `RedisDistributedRateLimiter` Lua script with `InMemoryDistributedRateLimiter` Fail-Open fallback) and Outbound Polly v8 5-stage resilience pipelines via `AddTradeFlowResiliencePipeline` and `ResilienceHttpExecutor` (ADR-0016).

### 2.6 Active Infrastructure & DevOps
- Docker Desktop installed for Testcontainers PostgreSQL, RabbitMQ, Kafka, and Redis integration tests (`[Trait("Category", "RequiresDocker")]`).
- Local multi-container development environment via `docker-compose.yml` (PostgreSQL 16, RabbitMQ 4 with Management UI, pgAdmin 4, Apache Kafka 3.8 KRaft, Kafka UI, Redis 7 Alpine, Jaeger all-in-one).
- Cloud-Native Containerization & Kubernetes (ADR-0017):
  - Multi-stage, non-root (`USER $APP_UID`) production Alpine [`Dockerfile`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/Dockerfile) with layer-cached NuGet dependencies.
  - Production Kubernetes manifests ([`deploy/k8s/tradeflow-k8s.yaml`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/deploy/k8s/tradeflow-k8s.yaml)) and local dev manifests ([`deploy/k8s/tradeflow-local.yaml`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/deploy/k8s/tradeflow-local.yaml)) specifying Liveness (`/health/live`) and Readiness (`/health/ready`) probes, zero-downtime RollingUpdate (`maxUnavailable: 0`, `maxSurge: 1`), PodDisruptionBudgets, and HorizontalPodAutoscalers.
  - Multi-schema EF Core migrations with dedicated history tables (`orders."__EFMigrationsHistory"` and `inventory."__EFMigrationsHistory"`) applied at container startup via [`DatabaseBootstrap.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Api/Common/DatabaseBootstrap.cs).
  - 3-stage GitHub Actions CI/CD Pipeline ([`.github/workflows/ci-cd.yml`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/.github/workflows/ci-cd.yml)) enforcing `-warnaserror`, fast Unit/Arch tests, real Testcontainers suite, and Docker image build.
- Frontend SPA & White-Label SSO (`src/TradeFlow.Web`, ADR-0018):
  - Vite + React 19 + TypeScript SPA featuring OAuth2 PKCE (`S256`) in-memory token storage (`InMemoryWebStorage` via `oidc-client-ts`), TanStack Query server-state caching, RFC 7807 `ProblemDetails` error parsing, and strict API CORS allowlist (`TradeFlowCorsPolicy`).
  - Custom Keycloak FreeMarker login/error theme (`docker/keycloak/themes/tradeflow`) sharing identical CSS Design Tokens (`tokens.css`) and brand mark for visually seamless SSO redirection via public client `tradeflow-web`.

### 2.7 Active Testing Infrastructure
- Active (151 backend tests + 5 frontend Vitest tests + 1 Playwright E2E spec):
  - Unit/Domain: xUnit, FluentAssertions (41 tests: Order lifecycle, Money arithmetic, InventoryItem invariants, AggregateRoot temporal audit tracking)
  - Unit/Application: NSubstitute, FluentAssertions (31 tests: Command/Query handlers including `GetOrders` tenant-scoping & `GetInventoryItems`, Validation behaviors, InventoryModule atomic reservation, deterministic lock ordering, CachingBehavior pipeline interceptor)
  - Architecture: NetArchTest.Rules (19 tests: Inward layer dependency flow, entity encapsulation, Anti-Backdoor rule, inter-module bounded context isolation, infrastructure isolation, IModuleDbContext enforcement)
  - Infrastructure Integration: Testcontainers for PostgreSQL, RabbitMQ, Kafka & Redis (35 tests: 29 Docker + 6 non-Docker)
  - API Contract, Security, Resilience, CORS & Cloud-Native Health: `WebApplicationFactory`, Testcontainers PostgreSQL & `FakeTimeProvider` (25 tests: 13 Docker + 12 non-Docker including strict CORS origin allowlisting, multi-tenant `GET /api/orders`, `GET /api/inventory`, and health probes)
  - Frontend Unit/Component & E2E: Vitest + React Testing Library (5 tests covering RFC 7807 ProblemDetails parsing, RBAC UI visibility, and 409 conflict banner) + Playwright E2E spec (`e2e/sso-order-flow.spec.ts` scaffolded for full browser SSO & order lifecycle verification).

---

## 3. Approved Architecture Decision Records (ADRs)

| ADR ID | Title | Status | Date |
| :--- | :--- | :--- | :--- |
| [ADR-0001](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0001-orderline-snapshot-and-identifier-reference.md) | OrderLine Snapshot and Identifier Reference Across Bounded Contexts | Accepted | 2026-09-28 |
| [ADR-0002](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0002-monolith-order-intake-transaction-boundary.md) | Monolith Order Intake Transaction Boundary & Aggregate Isolation | Accepted | 2026-09-28 |
| [ADR-0003](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0003-in-process-mediator-and-pipeline-behaviors.md) | In-Process Application Dispatching via MediatR & Pipeline Behaviors | Accepted | 2026-09-29 |
| [ADR-0004](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0004-persistence-mapping-complex-types-backing-fields.md) | Persistence Mapping with EF Core Complex Types and Backing Fields | Accepted | 2026-09-29 |
| [ADR-0005](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0005-minimal-apis-and-problem-details.md) | Feature-Oriented Minimal APIs and RFC 7807 Problem Details | Accepted | 2026-10-04 |
| [ADR-0006](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0006-architectural-fitness-functions-and-rules.md) | Automated Architectural Fitness Functions via NetArchTest | Accepted | 2026-10-06 |
| [ADR-0007](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0007-modular-monolith-schema-isolation-and-module-contracts.md) | Modular Monolith PostgreSQL Schema Isolation and Module Contracts | Accepted | 2026-10-06 |
| [ADR-0008](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0008-modular-infrastructure-and-dedicated-dbcontexts.md) | Modular Infrastructure Organization and Dedicated DbContexts | Accepted | 2026-10-07 |
| [ADR-0009](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0009-pessimistic-locking-and-deterministic-ordering-for-inventory.md) | Pessimistic Row Locking and Deterministic Ordering for Inventory Contention | Accepted | 2026-10-07 |
| [ADR-0010](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0010-generic-unit-of-work-shared-connection-and-retry-policy.md) | Generic Unit of Work over Shared Connection and Fail-Fast Transaction Boundaries | Accepted | 2026-10-13 |
| [ADR-0011](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0011-transactional-outbox-pattern-and-idempotent-consumers.md) | Transactional Outbox Pattern with Polling Publisher (SKIP LOCKED) and Idempotent Consumers | Accepted | 2026-10-13 |
| [ADR-0012](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0012-high-throughput-event-streaming-apache-kafka.md) | High-Throughput Event Streaming with Apache Kafka (KRaft), Partition Key Ordering, and Dead-Letter Topics | Accepted | 2026-10-28 |
| [ADR-0013](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0013-identity-oauth2-oidc-rbac-and-tenant-isolation.md) | Stateless Federated Identity (OAuth2 / OIDC), RBAC & BOLA/IDOR Tenant Isolation | Accepted | 2026-10-30 |
| [ADR-0014](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0014-distributed-caching-cache-aside-stampede-protection-and-pipeline-behavior.md) | Distributed Caching (Cache-Aside), Single-Flight Stampede Protection & MediatR Pipeline Behavior | Accepted | 2026-11-03 |
| [ADR-0015](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0015-distributed-tracing-opentelemetry-w3c-outbox-propagation.md) | End-to-End Distributed Tracing with OpenTelemetry, W3C TraceContext, and Outbox Propagation | Accepted | 2026-11-03 |
| [ADR-0016](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0016-resilience-rate-limiting-circuit-breaker-bulkhead.md) | Edge Rate Limiting, Load Shedding, and Outbound Resilience Pipelines with Polly v8 & Redis | Accepted | 2026-11-12 |
| [ADR-0017](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0017-cloud-native-readiness-health-probes-graceful-shutdown-migrations-cicd.md) | Cloud-Native Readiness: Liveness/Readiness Probes, Graceful Shutdown, Multi-Schema EF Core Migrations, Multi-Stage Containerization & CI/CD | Accepted | 2026-11-16 |
| [ADR-0018](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0018-frontend-architecture-spa-pkce-white-label-keycloak-testing.md) | Frontend SPA Architecture, OAuth2 PKCE In-Memory Auth, White-Label Keycloak Theme & Testing Pyramid | Accepted | 2026-11-16 |

---

## 4. Known Technical Debt & Intentional Compromises

| Item | Context / Rationale | Target Resolution Stage |
| :--- | :--- | :--- |
| Single Database Transaction across Use Case | Accepted per ADR-0002 to avoid premature distributed outbox/messaging complexity in Phase 1/2. | Resolved in Phase 3 (Outbox & RabbitMQ) |
| In-Memory Inventory Fake | Replaced with full `InventoryItem` aggregate root, `IInventoryRepository`, `IInventoryModule`, and PostgreSQL schema isolation (`inventory.inventory_items`). | Resolved (Phase 2) |
| DatabaseBootstrap Runtime Schema Initialization | Replaced runtime `CreateTablesAsync()` with automated EF Core multi-schema migrations with isolated history tables (`orders."__EFMigrationsHistory"`, `inventory."__EFMigrationsHistory"`). | Resolved in Phase 9 (ADR-0017) |
| Keycloak Login Quick-Fill Sandbox Buttons & Unverified E2E Suite | `login.ftl` includes quick-fill sandbox buttons for dev convenience, and Playwright E2E (`sso-order-flow.spec.ts`) is scaffolded pending Chromium execution & developer React fundamentals review. | Before Production / Capstone |

---

## 5. Current Focus & Next Milestones

- **Immediate Completed Task**: Phase 10 Frontend Architecture Baseline, Transparent Keycloak White-Label SSO (`tradeflow-web` PKCE + `loginTheme: tradeflow` + shared `tokens.css`), Backend Read APIs (`GET /api/orders`, `GET /api/inventory`) + Strict CORS Allowlist (`ADR-0018`). Verified end-to-end manually in browser (Keycloak SSO redirect -> Dashboard -> Order lifecycle).
- **Next Planned Learning Step**: Phase 11 — Choose between **Performance & Load Testing (k6)**, **Infrastructure as Code (Terraform)**, or **Chaos Engineering & Capstone Staff Interview Defense** (while the developer studies React fundamentals offline).
