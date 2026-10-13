# ADR-0010: Generic Unit of Work over Shared Connection and Fail-Fast Transaction Boundaries

- **Status**: Accepted
- **Date**: 2026-10-13
- **Deciders**: Developer & AI Mentor (Lead Architect)
- **Relevant Issues / Context**: Follow-up to ADR-0008 (dedicated DbContexts) and ADR-0009 (pessimistic locking). Refines cross-module transaction coordination and resilience strategy in `UnitOfWork.ExecuteInTransactionAsync`.

---

## 1. Context & Background
ADR-0008 isolated bounded contexts into dedicated `DbContext` classes (`OrdersDbContext`, `InventoryDbContext`). ADR-0009 introduced pessimistic row locks (`SELECT FOR UPDATE`) with deterministic ordering to eliminate inventory contention and deadlocks. 

When coordinating these two modules in a single atomic order-submission use case, two engineering forces collided:
1. **Code Scalability**: Hardcoding specific DbContexts in `UnitOfWork` violated the Open/Closed Principle. Adding a 3rd or 4th module would require cascading `DbContext` parameters and unmanageable nested transaction blocks.
2. **Resilience vs. State Integrity**: Configuring EF Core's `EnableRetryOnFailure` causes the ORM to attempt transparent automatic retries of failed user transactions. However, transactions that mutate in-memory aggregates and acquire pessimistic locks behave unpredictably when transparently re-executed.

---

## 2. Problem Statement
1. How do we allow `UnitOfWork` to coordinate an arbitrary number of module contexts without knowing them by name ($O(1)$ complexity relative to module count)?
2. Is transparent ORM-level transaction retrying (`NpgsqlRetryingExecutionStrategy`) appropriate for mission-critical transactional operations involving pessimistic row locks and stateful aggregate mutations?

---

## 3. Options Considered

### Option A: Shared Scoped Connection with Transparent ORM Retries
- Enlist all contexts in a single physical connection; chain `strategy.ExecuteAsync` and reset `ChangeTracker.Clear()` between retry attempts.
- *Pros*: Handles momentary network blips without returning errors to callers.
- *Cons*:
  - **Ghost In-Memory Mutations**: Relies on strict developer discipline to reload every aggregate inside the transaction delegate; any external aggregate state becomes corrupt on attempt 2.
  - **Amplified Lock Contention**: Retrying under pessimistic contention holds locks longer, cascading database connection pool starvation and tail latency.
  - **High Cognitive Overhead**: Convoluted execution pipeline and tracker-clearing gymnastics.

### Option B: Shared Scoped Connection with Fail-Fast Atomic Transactions (Selected)
- All module `DbContext`s in a request scope share one physical `NpgsqlConnection`. `UnitOfWork` accepts `IEnumerable<IModuleDbContext>`, opens a transaction on the primary context, and enlists the rest via `UseTransactionAsync`.
- **Fail-Fast Boundary**: Disable ORM-level transparent transaction retries. If a database error or network failure occurs, the transaction rolls back immediately and throws.
- **Boundary-Level Resilience**: Retries belong at the protocol edge (e.g., HTTP `Idempotency-Key` or consumer message redelivery), where a retry is dispatched as a brand new request with a clean, isolated DI scope.
- *Pros*:
  - Simple, robust, battle-tested production model (standard in banking and payment systems like Stripe).
  - Enforces the **Narrow Transaction Principle**: invariants validated upfront with zero locks; transaction held only during row-locking and writes.
  - Zero risk of in-memory state contamination or duplicate side effects.
  - $O(1)$ scalability: adding a module requires only registering its `DbContext : IModuleDbContext`.
- *Cons*: Transient network glitches result in an explicit error (HTTP 500 / 409 / 503) that the caller must retry.

### Option C: Single Monolithic DbContext
- Rejected: Loses compile-time boundary protection against cross-module joins (ADR-0008).

### Option D: Distributed Transactions (`TransactionScope` / 2PC)
- Rejected: Heavy, brittle, poor PostgreSQL / Npgsql support.

---

## 4. Decision
We select **Option B: Shared Scoped Connection with Fail-Fast Atomic Transactions**.

1. **Shared Connection**: In `DependencyInjection.cs`, `AddNpgsqlModuleDbContexts` registers a single scoped `DbConnection`. All module `DbContext`s implement `IModuleDbContext` and share this connection.
2. **Generic Fail-Fast UnitOfWork**: `UnitOfWork` receives `IEnumerable<IModuleDbContext>`. It begins a single transaction on the primary context and enlists all other contexts via `UseTransactionAsync`. Any failure triggers an immediate rollback and rethrow.
3. **Narrow Transaction Principle**: Application handlers validate domain invariants upfront outside the transaction (`order.ValidateCanSubmit()`). The transaction is opened exclusively to acquire pessimistic locks, mutate aggregate state, and commit.
4. **Resilience at the Edge**: Transparent ORM retries on write transactions are banned. Retries are delegated to API clients utilizing idempotency mechanisms.

---

## 5. Trade-offs & Consequences
- **Positive Consequences (Gains)**:
  - Clean, auditable, and production-hardened code without hidden retry loops.
  - Absolute atomicity across all monolith modules.
  - Full protection against duplicate allocations and in-memory corruption.
  - Fully scalable to $N$ modules without modifying `UnitOfWork`.
- **Negative Consequences (Costs / Liabilities)**:
  - All participating modules must reside within the same physical PostgreSQL database (expected in Phase 2 modular monolith).
  - Clients must handle transient errors by retrying at the HTTP/API level.

---

## 6. Conditions for Revisiting
- **Phase 3 (Service Extraction / Outbox)**: When a bounded context is split into an independent database or microservice, the shared-connection unit of work will be replaced by the Transactional Outbox pattern and asynchronous messaging (RabbitMQ).
