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

### 2.7 Active Testing Infrastructure
- Active (137 automated tests):
  - Unit/Domain: xUnit, FluentAssertions (41 tests: Order lifecycle, Money arithmetic, InventoryItem invariants, AggregateRoot temporal audit tracking)
  - Unit/Application: NSubstitute, FluentAssertions (27 tests: Command handlers, Validation behaviors, InventoryModule atomic reservation, deterministic lock ordering, CachingBehavior pipeline interceptor)
  - Architecture: NetArchTest.Rules (19 tests: Inward layer dependency flow, entity encapsulation, Anti-Backdoor rule, inter-module bounded context isolation, infrastructure isolation, IModuleDbContext enforcement)
  - Infrastructure Integration: Testcontainers for PostgreSQL, RabbitMQ, Kafka & Redis (33 tests: Order persistence, OCC rowversion verification, InventoryItem persistence, hot SKU high-concurrency contention without OCC exceptions, multi-item opposing order deadlock prevention, cross-context atomic commit, business failure rollback, database exception fail-fast rollback, outbox message atomic persistence, outbox rollback isolation, outbox state mutations, outbox batch processing, poison message isolation after max retries, concurrent multi-worker `SKIP LOCKED` disjoint batching, RabbitMQ Topic Exchange exact/wildcard routing with Publisher Confirms, Consumer NACK Dead-Letter Queue routing, push-based consumer dispatch and inbox deduplication, Kafka deterministic Partition Key routing, strict chronological consumption per ProductId, non-blocking Poison Pill DLT isolation, Redis distributed cache get/set/delete, `ResultJsonConverterFactory` unwrapped DTO serialization, stampede single-flight mutex protection under 50 concurrent requests, multi-pod atomic Redis Lua rate limiting, OpenTelemetry W3C traceparent outbox capture, and distributed tracing propagation)
  - API Contract, Security & Resilience: Microsoft.AspNetCore.Mvc.Testing (`WebApplicationFactory`), Testcontainers PostgreSQL & `FakeTimeProvider` (17 tests: HTTP contracts, RFC 7807 problem details, full lifecycle with real cross-context database commits, Keycloak OAuth2/OIDC JWT authentication, RBAC authorization, BOLA/IDOR tenant isolation, Polly v8 401 non-retry, 503 transient retry, Circuit Breaker full state machine with `FakeTimeProvider`, in-memory rate limiter window expiration, multi-tenant noisy neighbor 429 isolation, and 503 fail-fast when Keycloak is unreachable)

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

---

## 4. Known Technical Debt & Intentional Compromises

| Item | Context / Rationale | Target Resolution Stage |
| :--- | :--- | :--- |
| Single Database Transaction across Use Case | Accepted per ADR-0002 to avoid premature distributed outbox/messaging complexity in Phase 1/2. | Resolved in Phase 3 (Outbox & RabbitMQ) |
| In-Memory Inventory Fake | Replaced with full `InventoryItem` aggregate root, `IInventoryRepository`, `IInventoryModule`, and PostgreSQL schema isolation (`inventory.inventory_items`). | Resolved (Phase 2) |
| DatabaseBootstrap Runtime Schema Initialization | Pragmatic local development and test bootstrap via `RelationalDatabaseCreator.CreateTablesAsync()`. | Target resolution in CI/CD & Deployment Migrations Phase |

---

## 5. Current Focus & Next Milestones

- **Immediate Completed Task**: Phase 8 Advanced Resilience, Fault Tolerance & Edge Protection (Polly v8 5-stage pipeline, Bulkhead, Circuit Breaker, `ResilienceHttpExecutor`, Load Shedding, Multi-Tenant Distributed Rate Limiting in Redis via Lua script with Fail-Open fallback, ADR-0016). 137/137 tests passing.
- **Next Planned Learning Step**: Phase 9: Cloud-Native Readiness, Production Operations, Liveness/Readiness Health Checks, Graceful Shutdown, EF Core Migrations & CI/CD Pipeline.
