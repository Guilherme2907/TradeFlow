# Session Checkpoint: Phase 3 Transactional Outbox Foundation & Dual-Write Resolution

- **Date**: 2026-10-13
- **Project Stage**: Phase 3 — Asynchronous Messaging & Transactional Outbox
- **Focus Area**: Dual-Write Problem Elimination via Transactional Outbox Pattern & SaveChanges Interceptor

---

## 1. Implementation Progress
- Introduced [`IHasDomainEvents`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Common/IHasDomainEvents.cs) to decouple domain event collection from concrete aggregate ID types.
- Updated [`AggregateRoot<TId>`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Common/AggregateRoot.cs) to implement `IHasDomainEvents`.
- Created [`OutboxMessage`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Modules/Orders/Persistence/Outbox/OutboxMessage.cs) entity encapsulating `Id`, `EventType`, `Payload` (JSON), `OccurredAt`, `ProcessedAt`, `Error`, and `RetryCount`.
- Configured EF Core mapping via [`OutboxMessageConfiguration`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Modules/Orders/Persistence/Configurations/OutboxMessageConfiguration.cs) targeting table `orders.outbox_messages` with `jsonb` payload and partial index `idx_orders_outbox_unprocessed WHERE processed_at IS NULL`.
- Implemented [`DomainEventsToOutboxMessagesInterceptor`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Modules/Orders/Persistence/Outbox/DomainEventsToOutboxMessagesInterceptor.cs) inheriting from `SaveChangesInterceptor` to atomically serialize uncommitted events and clear them from aggregate roots before committing.
- Registered outbox interceptor in [`DependencyInjection.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/DependencyInjection.cs) across scoped Npgsql and in-memory test paths.
- Added comprehensive integration tests in [`OutboxMessagePersistenceTests`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.Infrastructure.IntegrationTests/Persistence/OutboxMessagePersistenceTests.cs) against Testcontainers PostgreSQL (92 tests passing across solution).

---

## 2. Architectural Decisions Made
- **ADR-0011 Formulated & Accepted**: Selected **Option C (Transactional Outbox with Polling Publisher using `SKIP LOCKED` and Idempotent Consumers)** over direct dual-write (rejected due to data loss/phantom events) and CDC/Debezium (deferred due to operational overhead).
- **Interceptor vs. Explicit Handler Approach**: Selected automatic persistence via EF Core `SaveChangesInterceptor` (Approach 1) ensuring zero domain event leaks across application command handlers, while retaining aggregate encapsulation.

---

## 3. Testing Decisions Made
- **Integration over Unit for Outbox**: Verified atomicity using real PostgreSQL instances via Testcontainers. Unit testing in-memory lists does not test database transaction rollback semantics or jsonb serialization behavior.
- **Explicit Rollback Isolation Test**: Designed a dedicated rollback test verifying that aborted transactions write neither orders nor outbox messages, proving the Dual-Write immunity.

---

## 4. Important Tests Added & What They Protect
1. `SubmitOrder_WhenSaved_ShouldAtomicallyPersistOrderAndOutboxMessageInPostgreSql`:
   - **Protected Behavior**: Calling `Order.Submit()` persists an `OutboxMessage` with `EventType = "OrderSubmittedEvent"`, correct JSON payload, and clears `order.DomainEvents`.
   - **Bug Prevention**: Prevents orders from being committed without their downstream integration events queued for asynchronous publication.
2. `SubmitOrder_WhenTransactionRollsBack_ShouldNotPersistOrderOrOutboxMessage`:
   - **Protected Behavior**: If an order transaction aborts, no outbox message remains in the database.
   - **Bug Prevention**: Prevents downstream workers from publishing phantom events for orders that never committed.
3. `OutboxMessage_MarkAsProcessed_ShouldUpdateProcessedAtAndClearError` & `OutboxMessage_RecordFailure_ShouldIncrementRetryCountAndSetError`:
   - **Protected Behavior**: State transitions and error tracking on the outbox entity.

---

## 5. What Was Understood Well
- Developer clearly explained the **Dual-Write Problem**: committing database changes and publishing to a broker cannot be atomic without distributed transactions; failure in either step creates irrecoverable inconsistencies (dropped events or phantom reactions).
- Developer articulated the necessity of **Idempotency** for consumers: because at-least-once delivery introduces duplicates on network disconnects or process restarts, consumers must deduplicate messages using unique IDs (Inbox pattern) or semantic state guards.

---

## 6. What Was Struggled With / Conceptual Gaps
- Initial unfamiliarity with **Two-Phase Commit (2PC / XA)** protocol: Mentor explained the prepare/commit phases, blocking coordinator locks, CAP availability penalty, and why modern distributed architectures reject 2PC in favor of Transactional Outbox + Eventual Consistency.

---

## 7. Next Learning Step
- Implement the **Outbox Polling Publisher Worker** (`OutboxPublisherBackgroundService`) using `SELECT ... FOR UPDATE SKIP LOCKED` and integrate with **RabbitMQ** using publisher confirms and dead-letter exchanges (DLX).
