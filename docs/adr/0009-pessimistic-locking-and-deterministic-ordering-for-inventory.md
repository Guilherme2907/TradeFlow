# ADR-0009: Pessimistic Row Locking and Deterministic Ordering for Inventory Contention

- **Status**: Accepted
- **Date**: 2026-10-07
- **Deciders**: Developer & AI Mentor (Lead Architect)
- **Relevant Issues / Context**: High-Contention Inventory Allocation, Race Conditions, Deadlock Elimination, and Production Testing

---

## 1. Context & Background
In B2B trading and high-volume e-commerce, inventory is a heavily contested, finite resource. When multiple buyers submit purchase orders simultaneously for the same high-demand product SKU ("Hot SKU" or flash-sale scenario), concurrent transactions compete to allocate stock.

Previously, `InventoryItem` relied solely on Optimistic Concurrency Control (OCC) with PostgreSQL's `xmin` system column. Under heavy contention (e.g., 20 concurrent transactions attempting to allocate 10 units each from an available pool of 1,000 units), all 20 transactions would read the same initial row version, but only 1 transaction would successfully commit; the remaining 19 transactions would be rejected with `DbUpdateConcurrencyException`, triggering retry storms, transaction log thrashing, and increased tail latency despite abundant physical inventory.

## 2. Problem Statement
1. How do we prevent retry storms and starvation when concurrent purchase orders contend for the same inventory stock?
2. How do we prevent cross-item deadlocks (the classic AB-BA circular lock dependency) when orders allocate multiple products simultaneously?
3. How do we preserve Domain-Driven Design invariants and domain events without leaking business logic into raw SQL queries?

## 3. Options Considered

### Option A: Optimistic Concurrency with Exponential Retries (Polly)
- Keep OCC tokens (`xmin`) and configure a retry policy on `DbUpdateConcurrencyException`.
- *Pros*: Completely non-blocking reads; minimal database locking overhead under low contention.
- *Cons*:
  - **Retry Storms**: Under high concurrency, 50 requests competing for a hot SKU thrash the database connection pool and CPU.
  - **Latency Spikes**: Waiting on exponential backoffs degrades buyer experience.
  - **Starvation**: Requests can exhaust their maximum retry attempts without succeeding even though inventory was available.

### Option B: Atomic SQL Counter Updates (`UPDATE ... WHERE available >= @qty`)
- Bypass loading aggregate roots into memory; execute raw `UPDATE inventory.inventory_items SET allocated_quantity = allocated_quantity + @qty WHERE available_quantity >= @qty`.
- *Pros*: High throughput; atomic row increment handled entirely by the database engine.
- *Cons*:
  - **Leaky Domain Logic**: Domain invariants (minimum quantity, release rules, stock availability calculation) are duplicated in SQL strings.
  - **Bypassed DDD Boundaries**: Domain events (`StockAllocatedDomainEvent`) cannot be raised naturally by the aggregate root.
  - **Inconsistent Abstractions**: Violates the principle that aggregates encapsulate state mutations and business rules.

### Option C: Pessimistic Row Locking (`SELECT FOR UPDATE`) with Deterministic Ordering (Selected)
- Acquire exclusive row-level locks on requested `InventoryItem` records using PostgreSQL's `SELECT *, xmin FROM inventory.inventory_items WHERE product_id = ANY(@productIds) ORDER BY product_id FOR UPDATE`.
- Group and sort requested items deterministically in ascending order of `ProductId` prior to acquiring locks.
- *Pros*:
  - **Zero Concurrency Exceptions**: Competing transactions queue serially in PostgreSQL; once unlocked, `Read Committed` isolation re-reads the committed row version so all valid reservations succeed.
  - **Mathematical Deadlock Elimination**: Ordering row locks globally by `product_id` prevents circular AB-BA wait states.
  - **Preserves Full DDD Encapsulation**: The full `InventoryItem` aggregate root is rehydrated into memory, executes `item.Allocate()`, and emits domain events.
- *Cons*:
  - **Serializer Bottleneck**: Throughput on an individual hot SKU is serialized by transaction duration and disk write latency. Requires keeping the transaction window as narrow as possible.

## 4. Decision
We choose **Option C: Pessimistic Row Locking with Deterministic Ordering**.

1. **Deterministic Ordering**: [`InventoryModule.ReserveStockAsync`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/Inventory/Services/InventoryModule.cs) aggregates items per product and sorts them by `ProductId.Value` in ascending order.
2. **Pessimistic Locking Query**: [`InventoryRepository.GetByProductIdsForUpdateAsync`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Modules/Inventory/Persistence/InventoryRepository.cs) executes `SELECT *, xmin ... ORDER BY product_id FOR UPDATE`, fetching and locking all required items in a single round-trip.
3. **Explicit System Column Projection**: Explicitly includes PostgreSQL's `xmin` system column in raw SQL projections to allow EF Core's row version change tracker to rehydrate without `InvalidOperationException`.
4. **Narrow Transaction Window**: All validation and request sorting occur before beginning the transactional lock scope, minimizing lock hold times and connection pool utilization.

## 5. Trade-offs & Consequences

### Positive Consequences (Gains)
- All concurrent allocations for a hot SKU succeed sequentially without throwing `DbUpdateConcurrencyException` or requiring retry logic.
- Cross-item deadlocks (AB-BA) are eliminated mathematically.
- Aggregate root invariant methods (`Allocate`, `Touch`) and domain events remain fully intact.
- High-concurrency integration tests verify zero exceptions under 10 concurrent requests and zero deadlocks under opposing multi-item orders.

### Negative Consequences (Costs / Liabilities)
- Hot SKUs serialize allocations, meaning maximum throughput is bounded by PostgreSQL transaction latency.
- Database connections from the pool are held while waiting for row locks. Requires enforcing the Narrow Transaction Principle (zero external network I/O while holding row locks).
