# Project Context — TradeFlow

This document captures the real-time architectural baseline, active modules, technological footprint, testing baseline, and technical debt of the repository. It is continuously updated across sessions.

---

## 1. Project Status Overview

| Attribute | Current Value |
| :--- | :--- |
| **Current Stage** | `PHASE 3: Asynchronous Messaging & Transactional Outbox (RabbitMQ) - COMPLETED` |
| **Application Status** | Phase 1, 2 & 3 Complete; Local Environment Setup & Phase 4 Preparation Active |
| **Active Architecture**| Modular Monolith (Clean Architecture + PostgreSQL Schemas + Transactional Outbox + RabbitMQ Push Consumers + Inbox Pattern) |
| **Primary Codebase** | .NET 10, C# |
| **Active Testing Baseline** | Pure Domain Unit (41), Application Unit (17), Architecture (17), API Integration Testcontainers (5), Infrastructure Integration Testcontainers (20) — 100 passing tests |
| **Repository Root** | `g:\Estudo\FullStack-Engineer-Plan\TradeFlow` |
| **Last Updated** | 2026-10-21 |

> [!NOTE]
> Phase 3 (Asynchronous Messaging & Transactional Outbox with RabbitMQ) is complete. Domain events (`IDomainEvent`) raised by aggregates (`IHasDomainEvents`) are automatically captured via EF Core's `DomainEventsToOutboxMessagesInterceptor` and atomically persisted to `orders.outbox_messages` (`jsonb` with partial index `WHERE processed_at IS NULL AND retry_count < 5`). `OutboxProcessor` and `OutboxPublisherBackgroundService` poll unprocessed batches inside an explicit transaction using `SELECT ... FOR UPDATE SKIP LOCKED` and dispatch events through `RabbitMqOutboxMessagePublisher` over a Singleton TCP `IConnection` (`RabbitMqConnectionManager`) and Scoped `IChannel` with Publisher Confirms (`Basic.Ack`) to a durable Topic Exchange (`tradeflow.events`) with Dead-Letter Exchange (`tradeflow.events.dlx`) topology.
> On the receiving side, push-based consumers (`RabbitMqConsumerBackgroundService` applying `BasicQosAsync(prefetchCount: 10)` backpressure) dispatch deliveries to event-specific consumers (`OrderSubmittedInvoiceConsumer`, `OrderCancelledNotificationConsumer`, `OrderLifecycleAuditConsumer`) deduplicated atomically through `IdempotentInboxConsumer` into `orders.inbox_messages` (ADR-0011). All 100 tests pass against real PostgreSQL and RabbitMQ in Docker via Testcontainers.

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

### 2.5 Active Infrastructure & DevOps
- Docker Desktop installed for Testcontainers PostgreSQL and RabbitMQ integration tests (`[Trait("Category", "RequiresDocker")]`).
- Local multi-container development environment via `docker-compose.yml` (PostgreSQL 16, RabbitMQ 4 with Management UI).

### 2.6 Active Testing Infrastructure
- Active (100 automated tests):
  - Unit/Domain: xUnit, FluentAssertions (41 tests: Order lifecycle, Money arithmetic, InventoryItem invariants, AggregateRoot temporal audit tracking)
  - Unit/Application: NSubstitute, FluentAssertions (17 tests: Command handlers, Validation behaviors, InventoryModule atomic reservation, deterministic lock ordering)
  - Architecture: NetArchTest.Rules (17 tests: Inward layer dependency flow, entity encapsulation, Anti-Backdoor rule, inter-module bounded context isolation, infrastructure isolation, IModuleDbContext enforcement)
  - API Contract & E2E: Microsoft.AspNetCore.Mvc.Testing (`WebApplicationFactory`), Testcontainers PostgreSQL (5 tests: HTTP contracts, RFC 7807 problem details, full lifecycle with real cross-context database commits)
  - Infrastructure Integration: Testcontainers for PostgreSQL & RabbitMQ (20 tests: Order persistence, OCC rowversion verification, InventoryItem persistence, hot SKU high-concurrency contention without OCC exceptions, multi-item opposing order deadlock prevention, cross-context atomic commit, business failure rollback, database exception fail-fast rollback, outbox message atomic persistence, outbox rollback isolation, outbox state mutations, outbox batch processing, poison message isolation after max retries, concurrent multi-worker `SKIP LOCKED` disjoint batching, RabbitMQ Topic Exchange exact/wildcard routing with Publisher Confirms, Consumer NACK Dead-Letter Queue routing, push-based consumer dispatch and inbox deduplication)

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

---

## 4. Known Technical Debt & Intentional Compromises

| Item | Context / Rationale | Target Resolution Stage |
| :--- | :--- | :--- |
| Single Database Transaction across Use Case | Accepted per ADR-0002 to avoid premature distributed outbox/messaging complexity in Phase 1/2. | Resolved in Phase 3 (Outbox & RabbitMQ) |
| In-Memory Inventory Fake | Replaced with full `InventoryItem` aggregate root, `IInventoryRepository`, `IInventoryModule`, and PostgreSQL schema isolation (`inventory.inventory_items`). | Resolved (Phase 2) |

---

## 5. Current Focus & Next Milestones

- **Immediate Completed Task**: Phase 3 Complete (Transactional Outbox + Polling Publisher + RabbitMQ Topic Exchange + Push Consumers + Idempotent Inbox Pattern). Verified with 100/100 tests.
- **Next Planned Learning Step**: Setup local multi-container development environment (`docker-compose.yml`), seed development database, and transition to **Phase 4: High-Throughput Event Streaming (Apache Kafka)**.
