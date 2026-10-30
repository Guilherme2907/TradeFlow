# ADR-0012: High-Throughput Event Streaming with Apache Kafka (KRaft), Partition Key Ordering, and Dead-Letter Topics

- **Status**: Accepted
- **Date**: 2026-10-28
- **Deciders**: Developer & AI Mentor (Lead Architect)
- **Relevant Issues / Context**: Phase 4 (High-Throughput Event Streaming). Introduces Apache Kafka alongside RabbitMQ to support high-frequency market trade streams, strict per-instrument chronological ordering, historical replay, and non-blocking poison pill isolation.

---

## 1. Context & Background
In Phase 3 ([ADR-0011](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0011-transactional-outbox-pattern-and-idempotent-consumers.md)), we introduced RabbitMQ to decouple transactional order lifecycle side effects (invoice generation, customer notifications, audit logging). RabbitMQ excels as a traditional message broker where messages are transiently buffered and deleted immediately upon consumer acknowledgment (`Basic.Ack`).

However, TradeFlow's expansion into institutional market data and real-time trade execution analytics introduces requirements that conflict with transient queue semantics:
1. **High-Throughput Ingestion**: Market trade executions and price ticks arrive in high-volume bursts that require sequential disk append semantics (OS page cache & zero-copy transfers) rather than per-message queue state management.
2. **Historical Replayability (Time-Travel)**: New downstream services (e.g., fraud detection ML pipelines, risk backtesting, regulatory audit projections) must be able to attach a new Consumer Group and replay historical trade events from `offset = 0` without impacting real-time consumers.
3. **Strict Chronological Ordering per Financial Instrument**: All trade executions and price updates for a specific asset (`ProductId` / `Sku`) must be processed in strict chronological sequence, even when multiple consumer pods scale horizontally.

---

## 2. Problem Statement
1. Should TradeFlow replace RabbitMQ entirely with Apache Kafka, or adopt a hybrid messaging architecture?
2. How do we partition the Kafka topic so that concurrent consumers never process price movements for the same `ProductId` out of order?
3. How do we prevent a malformed or unprocessable message (*Poison Pill*) from causing **Head-of-Line Blocking** across an entire Kafka partition?

---

## 3. Options Considered

### 3.1 Messaging Architecture: Kafka-Only vs. Hybrid (RabbitMQ + Kafka)
- **Option A: Replace RabbitMQ with Kafka for all workloads**
  - *Pros*: Single messaging infrastructure to operate.
  - *Cons*: **Rejected**. Kafka lacks native per-message acknowledgment, individual message retry delays, and flexible wildcard exchange routing. A single slow task or failing order in Kafka blocks the entire partition (Head-of-Line Blocking).
- **Option B: Hybrid Architecture — RabbitMQ for Task Queues + Kafka for Event Streaming (Selected)**
  - *Pros*: Uses each tool for its core architectural strength: RabbitMQ handles discrete point-to-point/pub-sub command workflows with per-message DLQs, while Apache Kafka (in ZooKeeper-less **KRaft mode**) handles high-throughput, append-only trade event streams with retention and replay.
  - *Cons*: Higher operational footprint (mitigated via Docker Compose locally and managed services/Operators in cloud environments).

### 3.2 Partition Key Selection for `tradeflow.market.trades.v1`
- **Option A: `OrderId` as Partition Key**
  - *Pros*: Even distribution across partitions.
  - *Cons*: **Rejected for Market Trade Streams**. Two consecutive orders for the same asset (`SKU-STEEL-001`) have different `OrderId` values and hash to different partitions (`hash(OrderId) % N`). Concurrent consumers reading different partitions at different speeds will process price updates out of chronological order.
- **Option B: `ProductId` as Partition Key (Selected)**
  - *Pros*: Guarantees that $\text{Hash}(\text{ProductId}) \pmod N$ always maps all trades of the same financial instrument to the **exact same partition**. Within a single partition, offsets are strictly sequential and consumed by at most one consumer instance per Consumer Group.
  - *Cons*: Potential for *Hot Partitions* (*Partition Skew*) if a single asset accounts for the vast majority of trading volume (mitigated via consumer batching or key salting if extreme skew arises).

---

## 4. Decision

We adopt **Apache Kafka (KRaft mode)** for Market Trade Event Streaming with the following architectural invariants:

### 4.1 Topic Topology
- **Primary Stream Topic**: `tradeflow.market.trades.v1` (3 partitions, `ProductId` partition key).
- **Dead-Letter Topic (DLT)**: `tradeflow.market.trades.dlq` (isolates poison pills to prevent partition stalling).

### 4.2 Producer Guarantees (`KafkaTradeStreamProducer`)
- **`IProducer<string, string>` Lifecycle**: Registered as a **Singleton** (`librdkafka` producers are thread-safe and multiplex connections internally; creating a producer per request is an anti-pattern).
- **`Acks = Acks.All`**: Broker waits for all In-Sync Replicas (ISR) to persist the record before acknowledging.
- **`EnableIdempotence = true`**: Guarantees exactly-once producer delivery semantics per partition session, preventing duplicate writes during transient network retries.

### 4.3 Consumer Guarantees & Poison Pill Isolation (`KafkaTradeStreamConsumerBackgroundService`)
- **`EnableAutoCommit = false`**: Offsets are never committed automatically on a timer.
- **Commit-After-Process**: `consumer.Commit(consumeResult)` is invoked strictly after the handler completes.
- **Non-Blocking Poison Pill Recovery**: If a record fails deserialization or violates non-transient domain rules, the consumer publishes the raw payload and exception diagnostic metadata to `tradeflow.market.trades.dlq` and **immediately commits the offset on the primary partition**, allowing subsequent offsets in that partition to proceed without interruption.

---

## 5. Trade-offs & Consequences
- **Positive Consequences**:
  - 100% chronological ordering guarantee per `ProductId` across distributed consumer instances.
  - Zero ZooKeeper dependency via KRaft metadata consensus.
  - Immunity to partition freezes when malformed messages arrive on the stream.
  - Full historical replay capability for future analytical consumer groups.
- **Negative Consequences / Operational Risks**:
  - Maximum consumer parallelism per Consumer Group is bounded by the partition count (`NumPartitions = 3`).
  - Hot SKUs can concentrate load on a single partition consumer.
