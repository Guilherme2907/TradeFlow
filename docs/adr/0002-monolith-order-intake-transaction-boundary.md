# ADR-0002: Monolith Order Intake Transaction Boundary & Aggregate Isolation

- **Status**: Accepted
- **Date**: 2026-09-28
- **Deciders**: Developer & AI Mentor (Lead Architect)
- **Relevant Issues / Context**: Phase 1 Core Domain & Clean Monolith Kickoff, [Open Question Q01](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/OPEN-QUESTIONS.md#L21)

---

## 1. Context & Background
When a buyer places a B2B purchase order in TradeFlow, two essential operations must occur:
1. The `Order` aggregate is created and persisted in the `Submitted` state.
2. The requested items must be reserved in `Inventory` so other buyers cannot take the available stock.

In Phase 1, TradeFlow runs as a single ASP.NET Core process backed by a single PostgreSQL database. However, the system is designed to evolve into a Modular Monolith (Phase 2) and an asynchronous, event-driven system with RabbitMQ (Phase 3).

## 2. Problem Statement
Should the creation of the `Order` and the reservation of stock in `Inventory` execute within:
1. A single atomic database transaction ($ACID$), or
2. Separate transactions via immediate eventual consistency / asynchronous messaging?

How do we design this in Phase 1 to guarantee consistency without prematurely introducing distributed systems overhead (e.g. Message Broker, Transactional Outbox, Sagas), while still maintaining domain boundaries so future separation does not require rewriting core aggregate logic?

## 3. Options Considered

### Option A: Premature Eventual Consistency with Outbox/Broker in Phase 1
- Immediately introduce RabbitMQ / Outbox table in Phase 1. The order is created in `Pending` state, an event is published, and an inventory worker asynchronously attempts stock reservation.
- *Pros*:
  - Completely decouples `Orders` and `Inventory` from Day 1.
  - No database lock contention on inventory tables during the HTTP order intake request.
- *Cons*:
  - **Premature Complexity Violation**: Violates the core evolutionary principle of the project. Introduces broker infrastructure, outbox dispatching, idempotency tracking, compensation workflows (if reservation fails, order must be cancelled), and eventual consistency UI handling before establishing the core domain baseline.

### Option B: Tightly Coupled Single-Transaction Entity Mutation
- A single DbContext transaction where the order handler loads both `Order` and `InventoryItem` entities directly, mutating both in a single method and calling `SaveChangesAsync()`.
- *Pros*:
  - Maximum simplicity.
- *Cons*:
  - Breaches DDD aggregate boundaries: one transaction mutates multiple aggregate roots directly.
  - Tight coupling at the entity and domain layer: `Orders` knows the internal structure and methods of `Inventory`.

### Option C: Pragmatic Application-Layer Transaction with Strict Aggregate Separation (Selected)
- Both operations execute within a single PostgreSQL database transaction in Phase 1 managed at the Application Use Case layer.
- `Order` and `Inventory` remain strictly separate Aggregate Roots with zero cross-aggregate references.
- The `CreateOrderCommandHandler` coordinates the flow via an abstraction (`IInventoryReservationService` or separate repository interfaces).
- *Pros*:
  - **Guaranteed Consistency**: Zero chance of "ghost orders" (orders placed without stock reserved).
  - **Domain Purity**: Domain aggregates remain completely decoupled. The `Order` aggregate does not know how `Inventory` is structured or persisted.
  - **Clear Evolutionary Path**: When transitioning to Phase 2 (Modular Monolith) and Phase 3 (Asynchronous Outbox & Messaging), only the Application layer orchestration changes (swapping the direct reservation call with an Outbox event / Saga), leaving the domain models 100% untouched.
- *Cons*:
  - Shared database transaction creates potential lock contention under high concurrent ordering of identical SKUs (accepted during Phase 1 baseline).

## 4. Decision
We select **Option C: Pragmatic Application-Layer Transaction with Strict Aggregate Separation**.

In Phase 1, `Order` creation and `Inventory` stock reservation will commit within the same atomic database transaction at the Application Use Case level. However, `Order` and `InventoryItem` will exist as strictly separate Aggregate Roots, interacting only through application-level contracts.

## 5. Trade-offs & Consequences

### Positive Consequences (Gains)
- Immediate strong consistency and predictable behavior without requiring RabbitMQ, Outbox polling, or distributed compensation logic in the first phase.
- Clean domain models protected against architectural leakage.

### Negative Consequences (Costs / Liabilities)
- Potential database row lock contention on hot inventory rows if many buyers order the same item simultaneously.
- Refactoring the application handler will be required when transitioning to Phase 3 (asynchronous messaging), but domain aggregates will remain intact.

## 6. Conditions for Revisiting
This decision will be revisited at **Phase 3 (Asynchronous Messaging & Outbox)** or earlier if high-concurrency performance testing in Phase 1 reveals unacceptable lock wait times on shared inventory records.
