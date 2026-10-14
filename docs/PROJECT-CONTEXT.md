# Project Context — TradeFlow

This document captures the real-time architectural baseline, active modules, technological footprint, testing baseline, and technical debt of the repository. It is continuously updated across sessions.

---

## 1. Project Status Overview

| Attribute | Current Value |
| :--- | :--- |
| **Current Stage** | `PHASE 3: Asynchronous Messaging & Transactional Outbox (RabbitMQ)` |
| **Application Status** | Phase 1 & 2 Complete; Phase 3 Outbox Interceptor & Persistence Active |
| **Active Architecture**| Modular Monolith (Clean Architecture + PostgreSQL Schemas + Transactional Outbox) |
| **Primary Codebase** | .NET 10, C# |
| **Active Testing Baseline** | Pure Domain Unit (41), Application Unit (17), Architecture (17), API Integration Testcontainers (5), Infrastructure Integration Testcontainers (12) — 92 passing tests |
| **Repository Root** | `g:\Estudo\FullStack-Engineer-Plan\TradeFlow` |
| **Last Updated** | 2026-10-13 |

> [!NOTE]
> Phase 3 (Asynchronous Messaging & Transactional Outbox) is underway. Domain events (`IDomainEvent`) raised by aggregates (`IHasDomainEvents`) are automatically captured via EF Core's `DomainEventsToOutboxMessagesInterceptor` and atomically persisted to `orders.outbox_messages` (PostgreSQL `jsonb` with partial index `WHERE processed_at IS NULL`) in the exact same physical transaction as domain state mutations (ADR-0011). This resolves the Dual-Write Problem. Concurrency and transactional boundaries remain fail-fast without transparent retries (ADR-0010). All 92 tests run against real PostgreSQL in Docker via Testcontainers.

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
- Asynchronous messaging transition (Phase 3): Transactional Outbox pattern implemented via EF Core `DomainEventsToOutboxMessagesInterceptor` and `orders.outbox_messages` table (ADR-0011).
- Outbox Polling Publisher via `BackgroundService` (`SKIP LOCKED`) and RabbitMQ broker dispatching in progress.

### 2.5 Active Infrastructure & DevOps
- Docker Desktop installed for Testcontainers PostgreSQL integration tests (`[Trait("Category", "RequiresDocker")]`).

### 2.6 Active Testing Infrastructure
- Active (92 automated tests):
  - Unit/Domain: xUnit, FluentAssertions (41 tests: Order lifecycle, Money arithmetic, InventoryItem invariants, AggregateRoot temporal audit tracking)
  - Unit/Application: NSubstitute, FluentAssertions (17 tests: Command handlers, Validation behaviors, InventoryModule atomic reservation, deterministic lock ordering)
  - Architecture: NetArchTest.Rules (17 tests: Inward layer dependency flow, entity encapsulation, Anti-Backdoor rule, inter-module bounded context isolation, infrastructure isolation, IModuleDbContext enforcement)
  - API Contract & E2E: Microsoft.AspNetCore.Mvc.Testing (`WebApplicationFactory`), Testcontainers PostgreSQL (5 tests: HTTP contracts, RFC 7807 problem details, full lifecycle with real cross-context database commits)
  - Infrastructure Integration: Testcontainers for PostgreSQL (12 tests: Order persistence, OCC rowversion verification, InventoryItem persistence, hot SKU high-concurrency contention without OCC exceptions, multi-item opposing order deadlock prevention, cross-context atomic commit, business failure rollback, database exception fail-fast rollback, outbox message atomic persistence, outbox rollback isolation, outbox state mutations)

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
| Single Database Transaction across Use Case | Accepted per ADR-0002 to avoid premature distributed outbox/messaging complexity in Phase 1/2. | Phase 3 (Asynchronous Outbox & RabbitMQ - In Progress) |
| In-Memory Inventory Fake | **Resolved in Phase 2**: Replaced with full `InventoryItem` aggregate root, `IInventoryRepository`, `IInventoryModule`, and PostgreSQL schema isolation (`inventory.inventory_items`). | Resolved (Phase 2) |

---

## 5. Current Focus & Next Milestones

- **Immediate Completed Task**: Transactional Outbox Pattern Phase 3 Foundation: `IHasDomainEvents`, `OutboxMessage`, `orders.outbox_messages` schema mapping with partial index, automatic `DomainEventsToOutboxMessagesInterceptor`, and Testcontainers integration tests verifying atomic persistence and rollback isolation (92 tests passing).
- **Next Planned Learning Step**: Phase 3 Polling Publisher: Implement .NET `BackgroundService` with `SELECT ... FOR UPDATE SKIP LOCKED` and RabbitMQ publisher confirms.
