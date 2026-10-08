# Project Context — TradeFlow

This document captures the real-time architectural baseline, active modules, technological footprint, testing baseline, and technical debt of the repository. It is continuously updated across sessions.

---

## 1. Project Status Overview

| Attribute | Current Value |
| :--- | :--- |
| **Current Stage** | `PHASE 2: Modular Monolith & Strict Boundary Enforcement` |
| **Application Status** | Phase 1 Complete; Phase 2 Modular Monolith (`Orders` + `Inventory`) Active |
| **Active Architecture**| Modular Monolith (Clean Architecture + PostgreSQL Schemas + Architecture Fitness Functions) |
| **Primary Codebase** | .NET 10, C# |
| **Active Testing Baseline** | Pure Domain Unit (41), Application Unit (16), Architecture (14), API Integration Testcontainers (5), Infrastructure Integration Testcontainers (2) — 78 passing tests |
| **Repository Root** | `g:\Estudo\FullStack-Engineer-Plan\TradeFlow` |
| **Last Updated** | 2026-10-07 |

> [!NOTE]
> Phase 2 is fully operational. The `Inventory` bounded context has been integrated with schema isolation (`inventory.inventory_items`), aggregate root invariant enforcement (`InventoryItem`), and explicit in-process contract facade (`IInventoryModule`). Architectural fitness functions via `TradeFlow.ArchitectureTests` mathematically enforce Clean Architecture inward dependencies, entity encapsulation, the Anti-Backdoor mediator rule, and bounded context module isolation. All integration tests (both infrastructure and API endpoints) run against real PostgreSQL in Docker via Testcontainers with zero SQLite reliance.

---

## 2. Architectural & Testing Footprint

### 2.1 Current Architecture Style
- **Status**: Modular Monolith with Clean Architecture (`TradeFlow.Domain`, `TradeFlow.Application`, `TradeFlow.Infrastructure`, `TradeFlow.Api`).
- **Enforcement**:
  - Relational schema partitioning in PostgreSQL: `orders` schema and `inventory` schema.
  - In-process module communication strictly through explicit contracts (`IInventoryModule`), prohibiting cross-context internal domain entity references.
  - Automated architecture fitness tests via `NetArchTest.Rules`.
  - Base `AggregateRoot<TId>` encapsulates temporal audit tracking (`CreatedAt`, `UpdatedAt`, `Touch()`) and optimistic concurrency tokens (`Version`).

### 2.2 Active Modules / Bounded Contexts
- `Orders` (Order aggregate root, OrderLine entity, order state transitions, Minimal APIs).
- `Inventory` (InventoryItem aggregate root, physical vs. allocated vs. available stock, atomic multi-item stock reservation facade `IInventoryModule`, OCC rowversion concurrency token).
- `Catalog` (Product identifier reference `ProductId` in Domain.Common, price snapshots on order lines).

### 2.3 Active Storage & Persistence
- PostgreSQL for relational persistence via EF Core (`Npgsql.EntityFrameworkCore.PostgreSQL`).
  - Dedicated `DbContext` per bounded context: `OrdersDbContext` (`orders` schema) and `InventoryDbContext` (`inventory` schema).
  - All integration tests execute against real PostgreSQL via Testcontainers (`postgres:16-alpine`), eliminating in-memory emulators.

### 2.4 Active Messaging & Integration
- Synchronous in-process module contract invocation (`IInventoryModule`) within the monolith transaction boundary (ADR-0007).
- Asynchronous messaging (RabbitMQ / Kafka) deferred to Phase 3 & 4.

### 2.5 Active Infrastructure & DevOps
- Docker Desktop installed for Testcontainers PostgreSQL integration tests (`[Trait("Category", "RequiresDocker")]`).

### 2.6 Active Testing Infrastructure
- Active (78 automated tests):
  - Unit/Domain: xUnit, FluentAssertions (41 tests: Order lifecycle, Money arithmetic, InventoryItem invariants, AggregateRoot temporal audit tracking)
  - Unit/Application: NSubstitute, FluentAssertions (16 tests: Command handlers, Validation behaviors, InventoryModule atomic reservation)
  - Architecture: NetArchTest.Rules (14 tests: Inward layer dependency flow, entity encapsulation, Anti-Backdoor rule, inter-module bounded context isolation)
  - API Contract & E2E: Microsoft.AspNetCore.Mvc.Testing (`WebApplicationFactory`), Testcontainers PostgreSQL (5 tests: HTTP contracts, RFC 7807 problem details, full lifecycle with real cross-context database commits)
  - Infrastructure Integration: Testcontainers for PostgreSQL (2 tests: Order persistence and Optimistic Concurrency Control rowversion verification)

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

---

## 4. Known Technical Debt & Intentional Compromises

| Item | Context / Rationale | Target Resolution Stage |
| :--- | :--- | :--- |
| Single Database Transaction across Use Case | Accepted per ADR-0002 to avoid premature distributed outbox/messaging complexity in Phase 1/2. | Phase 3 (Asynchronous Outbox & RabbitMQ) |
| In-Memory Inventory Fake | **Resolved in Phase 2**: Replaced with full `InventoryItem` aggregate root, `IInventoryRepository`, `IInventoryModule`, and PostgreSQL schema isolation (`inventory.inventory_items`). | Resolved (Phase 2) |

---

## 5. Current Focus & Next Milestones

- **Immediate Completed Task**: Phase 2 Modular Monolith Foundation, Inventory Bounded Context, and Architecture Fitness Tests.
- **Next Planned Learning Step**: Phase 2 Deepening — Handling Inventory Concurrency & Race Conditions (OCC vs. pessimistic locking under high load), Stock Replenishment, and preparing for the Phase 3 Transactional Outbox transition.
