# ADR-0011: Transactional Outbox Pattern with Polling Publisher (SKIP LOCKED) and Idempotent Consumers

- **Status**: Accepted
- **Date**: 2026-10-13
- **Deciders**: Developer & AI Mentor (Lead Architect)
- **Relevant Issues / Context**: Launch of Phase 3 (Asynchronous Messaging & Decoupling). Solves the Dual-Write Problem between PostgreSQL and the message broker (RabbitMQ).

---

## 1. Context & Background
In Phase 2, `Orders` and `Inventory` were coordinated synchronously within a local transaction boundary in the modular monolith. In Phase 3, order submission must trigger downstream side effects (e.g., payment capture, invoice generation, customer notifications, external regulatory reporting).

Attempting to execute `_db.SaveChangesAsync()` followed by `_bus.PublishAsync()` introduces the **Dual-Write Problem**:
- If the database commit succeeds but the message broker publish fails (or the application crashes), the event is lost forever and downstream processes never execute (data inconsistency).
- If the broker publish executes first and the database transaction fails/rolls back, downstream systems react to a phantom order that was never committed.
- Two-Phase Commit (2PC / XA) is rejected: it is blocking, multiplies lock hold times by network latency, couples system availability (CAP theorem), and is unsupported by modern cloud message brokers.

---

## 2. Problem Statement
1. How do we guarantee that whenever an order is committed in PostgreSQL, an integration event is guaranteed to be published to RabbitMQ (*at-least-once delivery*), without distributed locks or 2PC?
2. How do we ensure concurrent background workers can poll outbox messages efficiently without table-level lock contention?
3. How do downstream consumers safely process messages given that *at-least-once* delivery implies inevitable duplicates?

---

## 3. Options Considered

### Option A: Direct Dual-Write (Application-Level Sequential Write)
- *Pros*: Zero infrastructure overhead; simplest possible code.
- *Cons*: **Rejected**. Guaranteed data loss or phantom state on process crashes, network partitions, or transient broker downtime.

### Option B: Change Data Capture (CDC via Debezium / Kafka Connect)
- Reads PostgreSQL Write-Ahead Log (WAL) directly and streams changes to Kafka/RabbitMQ.
- *Pros*: Zero application-level polling queries on PostgreSQL; near zero publishing latency.
- *Cons*: Requires heavy operational infrastructure (Debezium engine, Kafka Connect clusters, ZooKeeper/KRaft), complex local development setup, and schema evolution overhead. Premature for Phase 3.

### Option C: Transactional Outbox with Polling Publisher (`SKIP LOCKED`) (Selected)
- Domain events raised by aggregates (`OrderSubmittedEvent`) are intercepted and written to an `orders.outbox_messages` table within the **exact same local PostgreSQL transaction** as the business mutation.
- A .NET `BackgroundService` polls unprocessed messages in batches using `SELECT ... FOR UPDATE SKIP LOCKED`, publishes them to RabbitMQ with publisher confirms, and marks them `processed_at = UtcNow`.
- *Pros*:
  - **Atomic Consistency**: The outbox record commits or rolls back atomically with the domain state.
  - **Non-Blocking Concurrency**: `FOR UPDATE SKIP LOCKED` allows multiple background worker instances or threads to poll the outbox concurrently without blocking each other or producing duplicate reads.
  - **Technology Simplicity**: 100% standard SQL and .NET; zero external agents or CDC infrastructure required.
- *Cons*: Introduces polling latency (e.g., 200ms–1s); outbox table requires a periodic cleanup / retention job.

---

## 4. Decision

We select **Option C: Transactional Outbox with Polling Publisher using `FOR UPDATE SKIP LOCKED`**, combined with **Idempotent Consumers**.

### 4.1 Outbox Schema (`orders.outbox_messages`)
```sql
CREATE TABLE orders.outbox_messages (
    id UUID PRIMARY KEY,
    event_type VARCHAR(255) NOT NULL,
    payload JSONB NOT NULL,
    occurred_at TIMESTAMPTZ NOT NULL,
    processed_at TIMESTAMPTZ NULL,
    error TEXT NULL,
    retry_count INT NOT NULL DEFAULT 0
);

-- Partial index optimizing the polling query to near-zero cost:
CREATE INDEX idx_outbox_unprocessed 
ON orders.outbox_messages (occurred_at) 
WHERE processed_at IS NULL;
```

### 4.2 Non-Blocking Polling Query
The background publisher executes:
```sql
SELECT id, event_type, payload 
FROM orders.outbox_messages 
WHERE processed_at IS NULL 
ORDER BY occurred_at 
LIMIT 50 
FOR UPDATE SKIP LOCKED;
```

### 4.3 Idempotency Strategy for Consumers
Because the Outbox pattern guarantees **At-Least-Once Delivery** (a worker crash after broker publish but before `processed_at` update causes message redelivery), consumers MUST be idempotent:
1. **Semantic / Natural Idempotency**: Domain state machines naturally ignore duplicate requests (e.g. `order.Cancel()` when already canceled).
2. **Inbox Pattern (Deduplication Table)**: For non-idempotent operations (balance debit, shipping notification), the consumer inserts the `message_id` into an `inbox_messages` table within the **same local transaction** as the business side-effect. A primary key collision aborts duplicate processing cleanly.

---

## 5. Trade-offs & Consequences
- **Positive Consequences (Gains)**:
  - 100% immunity to the Dual-Write problem.
  - Independent availability: orders can be placed even if RabbitMQ is temporarily down; the outbox buffers messages until the broker recovers.
  - High polling throughput with zero lock contention across worker replicas via `SKIP LOCKED`.
- **Negative Consequences (Costs / Liabilities)**:
  - Eventual consistency: downstream consumers process events asynchronously with sub-second delay rather than synchronously.
  - Database disk growth: processed messages must be archived or pruned via a retention job.

---

## 6. Conditions for Revisiting
- **Phase 4 (Kafka Event Streaming)**: If event publication throughput exceeds hundreds of thousands of events per second where database polling becomes a bottleneck, evaluate migrating to log-based CDC (Debezium).
