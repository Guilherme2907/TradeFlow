# Session Checkpoint: Phase 3 RabbitMQ Messaging, Push Consumers & Inbox Pattern

- **Date**: 2026-10-21
- **Project Stage**: Phase 3 — Asynchronous Messaging & Transactional Outbox (RabbitMQ)
- **Focus Area**: Push-Based Consumers (`BasicConsumeAsync`), Backpressure (`BasicQosAsync`), AMQP Topology, Idempotent Inbox Pattern & E2E Integration

---

## 1. Implementation Progress
- Implemented `IIntegrationEventConsumer` interface and registered 3 domain event consumers in `src/TradeFlow.Infrastructure/Modules/Orders/Consumers/`:
  - [`OrderSubmittedInvoiceConsumer`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Modules/Orders/Consumers/OrderSubmittedInvoiceConsumer.cs): Listens for `orders.OrderSubmittedEvent` on `orders.invoice-generation` queue.
  - [`OrderCancelledNotificationConsumer`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Modules/Orders/Consumers/OrderCancelledNotificationConsumer.cs): Listens for `orders.OrderCancelledEvent` on `orders.customer-notification` queue.
  - [`OrderLifecycleAuditConsumer`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Modules/Orders/Consumers/OrderLifecycleAuditConsumer.cs): Listens for wildcard `orders.*` on `orders.audit-log` queue.
- Implemented [`RabbitMqConsumerBackgroundService`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Messaging/RabbitMq/RabbitMqConsumerBackgroundService.cs):
  - Long-lived background service orchestrating push consumption over a dedicated `IChannel` per consumer.
  - Declares durable queue topology with Dead-Letter Exchange (`tradeflow.events.dlx`) and routing bindings.
  - Applies backpressure via `BasicQosAsync(prefetchCount: 10)` to prevent memory saturation.
  - Dispatches incoming messages into an isolated `IServiceScope` for processing.
  - Coordinates with [`IdempotentInboxConsumer`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Modules/Orders/Persistence/Inbox/IdempotentInboxConsumer.cs) for deduplication (`orders.inbox_messages`).
  - Sends `BasicAckAsync` upon success or idempotency duplicate skip; sends `BasicNackAsync(requeue: false)` upon failure to route poison messages to DLQ.
- Added comprehensive end-to-end integration test in [`RabbitMqOutboxIntegrationTests`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.Infrastructure.IntegrationTests/Messaging/RabbitMqOutboxIntegrationTests.cs):
  - `RabbitMqConsumerBackgroundService_WhenOrderIsSubmittedAndCancelled_ShouldConsumePushEventsAndRecordInboxEntries`
  - Validates full transactional flow: Aggregate -> Domain Events -> Outbox Interceptor -> PostgreSQL Outbox -> OutboxProcessor -> RabbitMQ Topic Exchange -> Push Consumers -> Inbox Deduplication.

---

## 2. Architectural Decisions Made
- **Push (`BasicConsumeAsync`) vs. Polling (`BasicGetAsync`)**:
  - Polling creates high CPU/network overhead and latency jitter.
  - Push-based event-driven consumption establishes a continuous AMQP channel subscription where the broker streams messages as soon as available.
- **Consumer Channel vs. Publisher Channel Lifecycle**:
  - Consumer channels must remain open across the entire application lifecycle because AMQP push consumer subscriptions and server-assigned delivery tags are strictly tied to that channel.
  - Publisher channels are scoped/ephemeral per polling batch to guarantee thread-safe publishing without channel frame interleaving.
- **Backpressure via `BasicQosAsync(prefetchCount: 10)`**:
  - Constrains the unacknowledged message buffer per consumer.
  - Prevents Out-Of-Memory (OOM) crashes during queue backlogs and ensures fair distribution across multiple consumer instances.
- **Idempotent Inbox Pattern Integration**:
  - Each consumer handles incoming messages through `IdempotentInboxConsumer.ExecuteAsync` using atomic `INSERT ... ON CONFLICT DO NOTHING` inside a local transaction.
  - Redelivered messages are safely acknowledged without re-executing side effects.

---

## 3. Testing Decisions Made
- **End-to-End Real Infrastructure Verification**:
  - Verified using Testcontainers with real PostgreSQL 16 and real RabbitMQ 4 instances in Docker.
  - Proved that wildcards (`orders.*`) and exact routing keys (`orders.OrderSubmittedEvent`) accurately deliver 6 distinct messages to the 3 consumer queues when an order is created, item added, submitted, and cancelled.
- **Avoiding Flaky Time Invariants**:
  - In-memory aggregate operations execute within sub-microsecond intervals, producing identical timestamps on Windows. Tests assert semantic equivalence of event types rather than strict causal timestamp ordering.

---

## 4. Important Tests Added & What They Protect
- `RabbitMqConsumerBackgroundService_WhenOrderIsSubmittedAndCancelled_ShouldConsumePushEventsAndRecordInboxEntries`:
  - **Protected Behavior**: Guarantees that when domain events are published via the Outbox processor, push-based consumers correctly receive, deserialize, deduplicate via Inbox, and acknowledge messages.
  - **Bug Prevention**: Prevents missing messages, deadlocks in consumer background loops, broker acknowledgment mismatches, or duplicate executions.

---

## 5. What Was Understood Well
- The user correctly reasoned about:
  - Why `Topic` exchanges are optimal for domain event routing across micro-services/modules.
  - Why connection instances must be Singleton to avoid TCP socket exhaustion.
  - Why consumer channels stay open for push delivery while publisher channels are short-lived.
  - The purpose and operational necessity of `BasicQosAsync` backpressure.

---

## 6. What Was Struggled With / Conceptual Gaps
- Initial confusion between consumer workers as polling loops vs. push event-driven listeners in AMQP.
- Distinguishing library-managed consumer mechanics (like MassTransit) from native `RabbitMQ.Client` v7 abstractions.

---

## 7. Next Learning Step
- Set up local containerized infrastructure (`docker-compose.yml`) and API bootstrap to run, test, and debug the entire application locally.
- Transition to **Phase 4: High-Throughput Event Streaming (Apache Kafka)**.
