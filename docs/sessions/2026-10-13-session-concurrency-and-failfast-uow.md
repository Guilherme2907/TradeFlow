# Session Checkpoint: Pessimistic Locking, Generic Unit of Work & Fail-Fast Boundaries

- **Date**: 2026-10-13
- **Project Stage**: Phase 2 — Modular Monolith & Strict Boundary Enforcement
- **Focus Area**: Pessimistic Locking under High Contention, Generic Cross-Module UnitOfWork, and Fail-Fast Transaction Boundaries

---

## 1. Implementation Progress
- Implemented **Pessimistic Row Locking (`SELECT *, xmin FROM inventory.inventory_items ... FOR UPDATE`)** with deterministic `ProductId` sorting in `InventoryRepository` (ADR-0009).
- Created generic, module-agnostic persistence coordination:
  - Introduced `IModuleDbContext` marker interface ([`IModuleDbContext.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Persistence/IModuleDbContext.cs)).
  - Implemented generic `UnitOfWork` ([`UnitOfWork.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Persistence/UnitOfWork.cs)) accepting `IEnumerable<IModuleDbContext>`, coordinating a single physical transaction over a shared scoped `DbConnection` via `UseTransactionAsync`.
  - Added shared scoped connection registration in `DependencyInjection.cs` (`AddNpgsqlModuleDbContexts`).
- Enforced the **Narrow Transaction Principle** in [`SubmitOrderCommandHandler.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/Orders/Commands/SubmitOrder/SubmitOrderCommand.cs): domain invariants validated upfront outside the transaction with zero database locks; transaction opened exclusively to acquire row locks, transition status, and commit.
- Eliminated transparent ORM transaction retries (`EnableRetryOnFailure`) in favor of fail-fast atomic transactions for critical operations.

---

## 2. Architectural Decisions Made
- **[ADR-0009](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0009-pessimistic-locking-and-deterministic-ordering-for-inventory.md)**: Pessimistic Row Locking and Deterministic Ordering for Inventory Contention.
  - Selected pessimistic row locking over OCC retry storms and atomic SQL updates.
  - Enforced deterministic sorting by `ProductId` to eliminate deadlocks (40P01).
- **[ADR-0010](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0010-generic-unit-of-work-shared-connection-and-retry-policy.md)**: Generic Unit of Work over Shared Connection and Fail-Fast Transaction Boundaries.
  - Decoupled `UnitOfWork` from concrete DbContext names ($O(1)$ complexity relative to module count).
  - Explicitly rejected transparent ORM transaction retries for pessimistic operations: prevents ghost in-memory state contamination, eliminates lock latency amplification, and delegates retries to the protocol edge (Idempotency-Keys).

---

## 3. Testing Decisions Made
- **Architecture Tests (3 new tests, total 17)**:
  - `OrdersInfrastructure_ShouldNotDependOn_InventoryPersistenceOrDomain`
  - `InventoryInfrastructure_ShouldNotDependOn_OrdersPersistenceOrDomain`
  - `ModuleDbContexts_ShouldImplement_IModuleDbContext`
- **Infrastructure Integration Tests with Docker Testcontainers (3 new tests, total 8)**:
  - `PessimisticLocking_UnderHighConcurrencyOnHotSku_ShouldExecuteAllReservationsSequentiallyWithoutExceptions`: Proves 10 concurrent requests reserve stock sequentially without OCC exceptions.
  - `PessimisticLocking_WithOpposingMultiItemOrder_ShouldPreventDeadlocksViaDeterministicSorting`: Proves tasks requesting `[B, A]` and `[A, B]` concurrently never deadlock.
  - `ExecuteInTransactionAsync_WhenOperationSucceeds_ShouldCommitBothModules`: Verifies cross-context atomic commit.
  - `ExecuteInTransactionAsync_WhenOperationReturnsFailure_ShouldRollbackBothModules`: Verifies cross-context rollback on business failure.
  - `ExecuteInTransactionAsync_WhenDatabaseExceptionOccurs_ShouldRollbackBothModulesAndRethrowFailFast`: Verifies immediate rollback and clean state preservation under infrastructure failure.
- **What Was NOT Tested**: In-memory DB fakes (SQLite/InMemory) were completely excluded from persistence tests to guarantee 100% PostgreSQL production parity.

---

## 4. Important Tests Added & What They Protect
- **`UnitOfWorkTests.ExecuteInTransactionAsync_WhenDatabaseExceptionOccurs_ShouldRollbackBothModulesAndRethrowFailFast`**:
  - *Protected Behavior*: Protects dual-module atomicity. If a connection drops after `orders` update, `inventory` cannot remain allocated.
  - *Bug Prevention*: Prevents phantom inventory allocations and partial commits.
- **`InventoryRepositoryTests.PessimisticLocking_UnderHighConcurrencyOnHotSku...`**:
  - *Protected Behavior*: Verifies that hot SKUs withstand 10+ concurrent transactions without concurrency exceptions.
  - *Bug Prevention*: Prevents flash-sale / promotion order drops.
- **`ModuleBoundaryTests.OrdersInfrastructure_ShouldNotDependOn_InventoryPersistenceOrDomain`**:
  - *Protected Behavior*: Protects schema isolation and prevents cross-module LINQ joins in EF Core.
  - *Bug Prevention*: Prevents developers from joining `orders.orders` with `inventory.inventory_items` at compile/CI time.

---

## 5. What Was Understood Well
- **OCC vs. Pessimistic Locking Trade-Off**: Independent analysis of why OCC collapses into retry storms under high contention on shared inventory rows.
- **Why Atomic SQL Updates Leak DDD Invariants**: Evaluated why `UPDATE ... SET quantity = quantity - @qty WHERE quantity >= @qty` bypasses domain aggregate encapsulation.
- **Narrow Transaction Principle**: Articulated why holding transactions during upfront validations unnecessarily starves database connections.
- **Fail-Fast in Critical Systems**: Realized that transparent ORM retries inside write transactions create in-memory corruption risks and that idempotency belongs at the edge.

---

## 6. What Was Struggled With / Conceptual Gaps
- **Transaction Lifespan of `SELECT FOR UPDATE`**: Initially questioned why `InventoryRepositoryTests` required an explicit transaction; clarified that pessimistic row locks auto-release immediately upon statement completion without an ambient transaction.
- **EF Core Retrying Strategy vs. User Transactions**: Encountered `NpgsqlRetryingExecutionStrategy` rejecting manual transactions. Explored execution strategy delegate nesting, then evolved the design to fail-fast transactions with boundary-level resilience.

---

## 7. Important Technical Concepts Explored
- **PostgreSQL Row-Level Locking (`FOR UPDATE`)**: Acquired exclusively within open transactions; locks rows until commit/rollback.
- **Deterministic Resource Ordering**: Sorting resources by key globally before acquiring locks mathematically eliminates circular dependency deadlocks ($A \rightarrow B$ vs. $B \rightarrow A$).
- **EF Core Connection Sharing**: Sharing a single physical `NpgsqlConnection` across multiple `DbContext` instances within a DI scope enables single-phase atomic commits across bounded contexts without distributed 2PC transactions.
- **Boundary Idempotency vs. ORM Retries**: Mission-critical transaction processing relies on fail-fast units of work, delegating retries to API idempotency mechanisms with clean execution scopes.

---

## 8. Intentionally Introduced Failures & Investigations
- **Failure Injected**: Interceptor injected simulated network drop / hardware failure during `InventoryDbContext.SaveChangesAsync()`.
- **Observed Behavior**: The unit of work aborted, issued an atomic rollback on both `orders` and `inventory`, and rethrew the exception.
- **Verification**: Verified via fresh context queries that `order.Status` remained `Draft` and `item.AllocatedQuantity` remained `0`.

---

## 9. Unanswered & Open Questions
- **Q01 (Dual-Write Problem vs. Transactional Outbox)**: Now the immediate next topic as we prepare to transition from monolith in-process transactions to asynchronous messaging in Phase 3.

---

## 10. Next Learning Step
- **Phase 3 Launch**: Exploring **The Dual-Write Problem** and designing the **Transactional Outbox Pattern** to decouple order submission from external side effects (notifications, trade compliance) without distributed transactions.
