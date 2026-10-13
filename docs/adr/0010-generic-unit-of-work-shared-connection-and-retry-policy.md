# ADR-0010: Generic Unit of Work over a Shared Connection and Retry-Safe Transaction Delegates

- **Status**: Accepted
- **Date**: 2026-10-13
- **Deciders**: Developer & AI Mentor (Lead Architect)
- **Relevant Issues / Context**: Follow-up to ADR-0008 (dedicated DbContexts) and ADR-0009 (pessimistic locking). Refines the cross-module transaction mechanism introduced in `UnitOfWork.ExecuteInTransactionAsync`.

---

## 1. Context & Background
ADR-0008 gave each bounded context its own `DbContext`. ADR-0009 requires `SELECT ... FOR UPDATE` to run inside an explicit, narrow transaction. The first `UnitOfWork` implementation hard-coded `OrdersDbContext` and `InventoryDbContext`, nesting one execution strategy and one transaction per module, plus an `if/else` on connection identity.

## 2. Problem Statement
1. **Scalability of the code**: every new module forced edits to `UnitOfWork` and deeper nesting (violates open/closed).
2. **Retry safety**: with `EnableRetryOnFailure`, EF Core re-executes the whole delegate after a transient failure. The database transaction is rolled back, but tracked entities keep their mutated in-memory state (e.g. `Order.Status == Submitted`, `InventoryItem.AvailableQuantity` already decreased). A naive retry either throws a domain exception or applies the allocation twice.

## 3. Options Considered

### Option A: Shared scoped connection + generic UoW (Selected)
- All module contexts in a request scope use the same `DbConnection`. `UnitOfWork` receives `IEnumerable<IModuleDbContext>`, opens one transaction on the first context and enlists the others (`UseTransactionAsync`).
- *Pros*: code is O(1) in number of modules; module isolation (separate DbContexts, no cross-module LINQ joins) is preserved; a single physical transaction.
- *Cons*: modules must live in one database (already true, ADR-0002); extraction to a microservice later requires replacing this with an outbox (Phase 3).

### Option B: Single DbContext with per-module configurations
- *Pros*: trivial transactions.
- *Cons*: loses compile-time protection against cross-module joins; would need to be compensated purely by architecture tests.

### Option C: Eventual consistency (Outbox/Saga)
- *Pros*: the target architecture for distributed modules.
- *Cons*: scheduled for Phase 3; premature now.

### Option D: `TransactionScope` over multiple connections
- Rejected: escalates to distributed transactions, poorly supported by Npgsql.

## 4. Decision
Option A, with the following rules:

1. Module DbContexts implement the marker `IModuleDbContext` and are registered through a single helper; they share one scoped `DbConnection`.
2. `UnitOfWork` is module-agnostic. Each context contributes its own execution strategy (folded, not hand-nested) and the transaction is committed or rolled back once.
3. **Retry contract**: the delegate passed to `ExecuteInTransactionAsync` may run more than once. It MUST load every aggregate it mutates *inside* the delegate. On every attempt after the first, the `UnitOfWork` clears the change trackers of all module contexts so the reload hits the database.
4. `SubmitOrderCommandHandler` loads and validates the order inside the delegate.
5. Module isolation is enforced by architecture tests: a module's `DbContext` and repositories may only reference entities of their own module.

## 5. Trade-offs & Consequences
- **Gains**: adding a module = one registration line; retries are deterministic; boundaries remain compile-time and CI enforced.
- **Costs**: the transaction now also covers the (non-locking) order read; handlers must follow the "load inside the delegate" convention (documented on `IUnitOfWork` and covered by a transient-failure integration test).

## 6. Conditions for Revisiting
- A module needs its own database or becomes a service: replace the shared-transaction path with Transactional Outbox (Phase 3).
- Number of concurrent transactions makes connection pool usage a bottleneck: reconsider the scope of the transaction or retry policy.
