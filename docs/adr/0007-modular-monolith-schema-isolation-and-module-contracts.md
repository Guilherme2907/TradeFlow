# ADR-0007: Modular Monolith PostgreSQL Schema Isolation and Explicit Module Contracts

- **Status**: Accepted
- **Date**: 2026-10-06
- **Deciders**: Developer & AI Mentor (Lead Architect)
- **Relevant Issues / Context**: Phase 2 Modular Monolith Boundary Enforcement & Inventory Context Design

---

## 1. Context & Background
As TradeFlow expands beyond the initial `Orders` slice to introduce the `Inventory` bounded context, we must establish how independent business modules within the monolith partition their data and communicate in-process.

Allowing modules to share a flat database namespace or invoke internal logic directly causes progressive architectural erosion ("Big Ball of Mud"), leading to cross-context queries, shared table locks, and circular dependencies.

## 2. Problem Statement
1. How should relational persistence be isolated between modules in a single PostgreSQL database?
2. How should cross-module operations (e.g., `Orders` reserving stock in `Inventory` during order submission) be orchestrated in-process?

## 3. Options Considered

### Option A: Flat `public` Schema & In-Process MediatR Command Bus
- Place all tables in PostgreSQL's default `public` schema (`public.orders`, `public.inventory_items`).
- Modules communicate by sending MediatR commands across boundaries (`sender.Send(new ReserveStockCommand(...))`).
- *Pros*: Familiar syntax; low initial setup.
- *Cons*:
  - **Leaky Joins**: Developers can effortlessly write LINQ queries joining `orders` and `inventory_items` directly in EF Core, erasing context boundaries.
  - **Opaque Communication**: MediatR commands hide dependencies behind a blind mediator; it is impossible to see at compile time what public API a module offers.
  - **Loss of Encapsulation**: Any module can send any internal command registered in DI.

### Option B: PostgreSQL Schema Isolation & Explicit Public Module Interfaces (Selected)
- Isolate relational tables into dedicated PostgreSQL schemas:
  - `orders.orders`, `orders.order_lines`
  - `inventory.inventory_items`
- Enforce in-process communication via **Explicit Public Module Interfaces** (`IInventoryModule`) taking dedicated, immutable contract DTOs (`ReserveStockRequest`).
- Keep module aggregates, internal repositories, and EF Core configurations strictly `internal` to the module.
- *Pros*:
  - **Zero Operational Overhead**: Runs inside the same single PostgreSQL instance and connection pool without multi-database cost.
  - **Hard Boundary Defense**: Clear separation prevents accidental cross-context relational joins.
  - **Compile-Time Discoverability & Type Safety**: `IInventoryModule` serves as an explicit, strongly typed public facade.
  - **Adapter Pattern Foundation**: If `Inventory` is ever extracted into a standalone microservice, `IInventoryModule` remains unchanged in `Orders`; only its DI registration swaps from an in-process class to an HTTP/gRPC client.
- *Cons*:
  - Requires defining explicit contract DTOs rather than sharing internal domain entities.

## 4. Decision
We select **Option B: PostgreSQL Schema Isolation & Explicit Public Module Interfaces**.

1. **Schema Partitioning**:
   - `OrderConfiguration` maps to schema `orders`.
   - `InventoryItemConfiguration` maps to schema `inventory`.
2. **Module Communication Contract**:
   - The Inventory module exposes an explicit interface `IInventoryModule`.
   - The Orders module consumes `IInventoryModule` through explicit contract DTOs, without referencing `InventoryItem` domain entities.
3. **Optimistic Concurrency Control**:
   - High-concurrency inventory allocation rows are protected by version concurrency tokens to eliminate race conditions and overselling.

## 5. Trade-offs & Consequences

### Positive Consequences (Gains)
- Clean, auditable boundaries inside a single monolithic deployment unit.
- Total protection against accidental relational joins between contexts.
- Simple database dumps per schema (`pg_dump -n inventory`) if extraction is ever required.

### Negative Consequences (Costs / Liabilities)
- EF Core migrations must ensure schemas exist prior to table creation (`CREATE SCHEMA IF NOT EXISTS`).

## 6. Conditions for Revisiting
This decision remains authoritative throughout Phase 2 and Phase 3 until asynchronous event-driven integration (RabbitMQ / Kafka) replaces synchronous in-process calls.
