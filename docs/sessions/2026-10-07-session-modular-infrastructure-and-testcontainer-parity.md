# Session Checkpoint: Modular Infrastructure, Dedicated DbContexts & Testcontainers Parity

- **Date**: 2026-10-07
- **Project Stage**: Phase 2 — Modular Monolith & Strict Boundary Enforcement
- **Focus Area**: Modular Infrastructure Reorganization, Dedicated DbContext Separation, PostgreSQL OCC Invariant Resolution, AggregateRoot Temporal Encapsulation, and Unified Testcontainers Testing

---

## 1. Implementation Progress
- Restructured [`TradeFlow.Infrastructure`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure) into module-specific folders:
  - `Modules/Orders/Persistence/`: [`OrdersDbContext.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Modules/Orders/Persistence/OrdersDbContext.cs), [`OrderRepository.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Modules/Orders/Persistence/OrderRepository.cs), and entity configurations.
  - `Modules/Orders/Gateways/`: [`InventoryReservationServiceAdapter.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Modules/Orders/Gateways/InventoryReservationServiceAdapter.cs) implementing `IInventoryReservationService`.
  - `Modules/Inventory/Persistence/`: [`InventoryDbContext.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Modules/Inventory/Persistence/InventoryDbContext.cs), [`InventoryRepository.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Modules/Inventory/Persistence/InventoryRepository.cs), and entity configurations.
- Coordinated multi-context persistence through [`UnitOfWork.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Persistence/UnitOfWork.cs).
- Refactored [`AggregateRoot<TId>`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Common/AggregateRoot.cs) to encapsulate temporal audit properties:
  - `CreatedAt` (`DateTimeOffset`), `UpdatedAt` (`DateTimeOffset?`), and `Touch(DateTimeOffset? timestamp = null)` method.
  - Refactored [`Order.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Orders/Order.cs) and [`InventoryItem.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Inventory/InventoryItem.cs) to inherit base timestamps and trigger `Touch()` on state changes.
- Refactored [`TradeFlowWebApplicationFactory.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.Api.IntegrationTests/Infrastructure/TradeFlowWebApplicationFactory.cs) to run all API contract tests against real containerized PostgreSQL via `Testcontainers.PostgreSql`, completely removing SQLite and in-memory providers from the solution.

## 2. Architectural Decisions Made
- **[ADR-0008: Modular Infrastructure and Dedicated DbContexts](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0008-modular-infrastructure-and-dedicated-dbcontexts.md)**: Approved separating the shared `TradeFlowDbContext` into `OrdersDbContext` and `InventoryDbContext`, eliminating cross-context LINQ joins at compile-time while grouping infrastructure assets by bounded context.
- **AggregateRoot Temporal Encapsulation**: Decided to place `CreatedAt`, `UpdatedAt`, and `Touch()` in `AggregateRoot<TId>` rather than on generic `Entity<TId>`. Aggregate roots represent the transactional boundary, lifecycle scope, and concurrency token anchors in DDD.
- **Zero-Emulator Integration Strategy**: Replaced SQLite in-memory with real PostgreSQL Testcontainers across both API integration tests and persistence tests. Ensures 100% production parity for schemas, JSON types, and MVCC/xmin row versions.

## 3. Testing Decisions Made
- **Testcontainers PostgreSQL for API Tests**: Migrated `TradeFlow.Api.IntegrationTests` to `Testcontainers.PostgreSql`. SQLite lacks PostgreSQL schema support and native `xmin` rowversioning, which previously masked real database behavior.
- **Schema Creation Handling with Multiple DbContexts**: Addressed EF Core's default `EnsureCreated()` behavior when multiple contexts share a database (where the second context skips creation because tables already exist) by explicitly ensuring schemas exist and invoking `RelationalDatabaseCreator.CreateTables()`.
- **Pure Domain Unit Tests for Aggregate Temporal Properties**: Added test cases verifying that `CreatedAt` is populated on initialization and `UpdatedAt` remains `null` until mutations invoke `Touch()`.

## 4. Important Tests Added & What They Protect
- [`OrderTests.CreatedAt_ShouldBeInitializedOnCreation_AndUpdatedAtNullInitially`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.Domain.UnitTests/Orders/OrderTests.cs#L201-L213): Protects initial timestamp semantics on order instantiation.
- [`OrderTests.OrderMutations_ShouldUpdateUpdatedAtTimestamp`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.Domain.UnitTests/Orders/OrderTests.cs#L215-L230): Protects audit timestamp progression across `AddLine` and `Submit`.
- [`InventoryItemTests.Allocate_Release_Replenish_ShouldUpdateUpdatedAtTimestamp`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.Domain.UnitTests/Inventory/InventoryItemTests.cs#L170-L193): Protects inventory modification tracking across all stock mutation methods.
- [`OrderEndpointTests`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.Api.IntegrationTests/Endpoints/OrderEndpointTests.cs) running on real PostgreSQL: Protects HTTP endpoints against genuine PostgreSQL relational constraints, schemas, and transactions.

## 5. What Was Understood Well
- The developer independently identified that `CreatedAt` and `UpdatedAt` duplicated on `Order` belonged in a base abstraction (`AggregateRoot`), enforcing consistency across domain models.
- The developer critically challenged the continued presence of SQLite in-memory in API integration tests, insisting on unified Testcontainers PostgreSQL for genuine production parity.
- The developer accurately reasoned through the Ports & Adapters decoupling that enables swapping in-process module adapters for remote transports without impacting the Application layer.

## 6. What Was Struggled With / Conceptual Gaps
- Relational OCC mechanics vs DDD Aggregate boundaries: Understanding why inserting rows into a child table (`orders.order_lines`) does not modify the parent table's row (`orders.orders`) or increment PostgreSQL's system `xmin` column unless the parent row itself is touched.

## 7. Important Technical Concepts Explored
- **PostgreSQL MVCC & `xmin` Concurrency**: How PostgreSQL uses the hidden `xmin` system column to record the transaction ID that created or modified a row, and how EF Core's `.IsRowVersion()` leverages `xmin` for OCC without requiring manual version incrementing.
- **Aggregate Root Encapsulation of Concurrency & Audits**: Why aggregate roots encapsulate state mutation timestamps and why calling `Touch()` bridges domain mutations with relational row versioning.
- **EF Core `EnsureCreated` vs `CreateTables` with Multiple Schemas**: Why calling `EnsureCreated()` on a secondary `DbContext` returns false if the database already has tables from another context, and how to reliably create partitioned module schemas in integration tests.

## 8. Intentionally Introduced Failures & Investigations
- **Failure**: `Concurrency_WhenTwoTransactionsModifySameOrder_ShouldThrowDbUpdateConcurrencyException()` failed when mutating child order lines because `orders.orders` was not touched.
- **Resolution**: Added `UpdatedAt` on `AggregateRoot` and called `Touch()` during child collection updates (`AddLine`, `RemoveLine`), guaranteeing EF Core issues an `UPDATE` on the aggregate root row and triggers PostgreSQL's `xmin` change.

## 9. Unanswered & Open Questions
- Dedicated currency table and currency exchange rates (catalog module evolution).
- Domain service vs repository interface in domain logic (deferred to advanced DDD exploration).

## 10. Professional English & Interview Practice Notes
- Refined articulating the distinction between domain entity boundaries and relational row boundaries in database concurrency.
- Articulated the trade-offs of in-memory test doubles (speed vs false confidence) vs containerized dependencies (fidelity vs execution overhead).

## 11. Next Learning Step
- Currency modeling as a formal entity or shared domain catalog table.
- Exploring Domain Events dispatching (in-memory vs Transactional Outbox) as we approach Phase 3.
