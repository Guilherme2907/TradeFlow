# ADR-0008: Modular Infrastructure Organization and Dedicated DbContexts per Bounded Context

- **Status**: Accepted
- **Date**: 2026-10-07
- **Deciders**: Developer & AI Mentor (Lead Architect)
- **Relevant Issues / Context**: Phase 2 Modular Monolith Boundary Hardening & Optimistic Concurrency Reliability

---

## 1. Context & Background
As TradeFlow scales its modular monolith architecture, physical separation of bounded contexts must extend through the Infrastructure layer. Previously, `TradeFlow.Infrastructure` was organized flatly by technical concern (`Repositories/`, `Configurations/`, `Services/`), and all entities were registered in a single monolithic `TradeFlowDbContext`.

Furthermore, empirical testing against PostgreSQL via Testcontainers revealed that adding child entities (`OrderLine`) to an aggregate root (`Order`) did not update the parent row in PostgreSQL. Consequently, PostgreSQL's `xmin` system column was not bumped, allowing concurrent modifications to proceed without triggering Optimistic Concurrency Control (OCC) conflicts.

## 2. Problem Statement
1. How do we prevent developers from executing accidental cross-context queries and LINQ joins in EF Core?
2. How do we organize the Infrastructure layer to maximize cohesion per bounded context and facilitate future service extraction?
3. How do we ensure that mutating child entities inside an aggregate root reliably triggers parent OCC rowversion tokens in PostgreSQL?

## 3. Options Considered

### Option A: Single Shared `DbContext` & Flat Technical Folders
- Keep all entities in `TradeFlowDbContext` with flat folders (`Repositories/`, `Configurations/`).
- *Pros*: Simple setup; single change tracker.
- *Cons*:
  - **Leaky Joins**: Developers can write LINQ queries joining `orders` and `inventory_items` directly.
  - **Low Cohesion**: Scattering module concerns across technical folders impairs discoverability and modular extraction.
  - **Single Point of Coupling**: Every module's schema and mapping rules live in one class.

### Option B: Dedicated Module Folders, Separate `DbContext`s, and Aggregate Version Touch (Selected)
- Restructure `TradeFlow.Infrastructure` by module (`Modules/Orders/`, `Modules/Inventory/`).
- Split persistence into dedicated `DbContext` classes:
  - `OrdersDbContext`: Maps exclusively to `orders.orders` and `orders.order_lines`.
  - `InventoryDbContext`: Maps exclusively to `inventory.inventory_items`.
- Coordinate in-process multi-module commits via `UnitOfWork : IUnitOfWork`.
- Touch a parent scalar property (`UpdatedAt = DateTimeOffset.UtcNow`) on `Order` whenever child lines are added or removed to trigger row-level `xmin` concurrency updates.
- *Pros*:
  - **Compile-Time Boundary Defense**: Impossible to join orders and inventory entities in EF Core.
  - **High Cohesion**: Each module's repositories, configurations, and adapters live in a dedicated folder.
  - **Rock-Solid Concurrency**: Parent rowversion / `xmin` tokens update reliably on child collection mutations.
- *Cons*:
  - Cross-context use cases require a coordinating unit of work in the monolith.

## 4. Decision
We select **Option B: Dedicated Module Folders, Separate `DbContext`s, and Aggregate Version Touch**.

1. **Folder Organization**: Group infrastructure components under `src/TradeFlow.Infrastructure/Modules/{ModuleName}/`.
2. **Context Isolation**: Replace monolithic `TradeFlowDbContext` with `OrdersDbContext` and `InventoryDbContext`.
3. **Concurrency Integrity**: Update `Order.UpdatedAt` on all child entity mutations (`AddLine`, `RemoveLine`), guaranteeing PostgreSQL `xmin` updates.

## 5. Trade-offs & Consequences

### Positive Consequences (Gains)
- Cross-context joins are eliminated at compile time.
- Extraction of any module to an independent microservice is simplified to relocating its module directory.
- Concurrency conflicts are accurately trapped by EF Core's `DbUpdateConcurrencyException`.

### Negative Consequences (Costs / Liabilities)
- Test harnesses sharing an in-memory SQLite connection must create tables explicitly per context (e.g., via `RelationalDatabaseCreator`).

## 6. Conditions for Revisiting
This pattern serves the modular monolith throughout Phase 2. In Phase 3, asynchronous event-driven integration (Transactional Outbox + RabbitMQ) will replace synchronous multi-context unit-of-work coordination.
