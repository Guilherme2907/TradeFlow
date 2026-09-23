# Open Architectural & Testing Questions Registry

This document tracks unresolved, challenging, or recurring architectural and testing questions. It ensures difficult concepts are systematically revisited, tested, and mastered rather than forgotten once discussed.

---

## Status Definitions

- `New`: Newly introduced question; not yet formally answered or debated by the developer.
- `Weak`: Attempted; significant gaps in trade-off analysis, failure behavior, or conceptual reasoning.
- `Needs Review`: Demonstrated understanding of basic theory, but struggles with edge cases or production recovery.
- `Good`: Solid explanation of mechanics and trade-offs; minor coaching required on edge scenarios.
- `Strong`: Confidently articulates the problem, alternatives, trade-offs, failure scenarios, and sunset criteria without AI assistance.

> **Rule**: Never delete a question merely because it was discussed in a session. Status transitions require verifiable demonstration of senior-level reasoning.

---

## Section A: Architectural & Distributed Systems Dilemmas

### Q01: Dual-Write Problem vs. Transactional Outbox
- **Question**: When a trade order is created, the system must persist the trade state to PostgreSQL and publish a `TradePlacedEvent` to RabbitMQ. If we execute `_db.SaveChangesAsync()` followed immediately by `_bus.PublishAsync()`, what specific failure modes occur, why is Two-Phase Commit (2PC) rejected in modern distributed systems, and how does the Transactional Outbox pattern solve this with at-least-once guarantees?
- **Topic**: Distributed Transactions & Data Consistency
- **Status**: `New`
- **Date Introduced**: 2026-09-22
- **Last Reviewed**: —
- **Notes**: Foundational question for distributed systems engineering. Must understand crash windows between DB commit and network dispatch.

---

### Q02: Message Queue (RabbitMQ) vs. Distributed Event Log (Kafka)
- **Question**: When should an architecture choose RabbitMQ over Apache Kafka, and vice versa? Specifically, compare them across message routing capabilities, consumption models (smart broker / dumb consumer vs. dumb broker / smart consumer), backpressure handling, message retention, and replayability.
- **Topic**: Messaging & Event Streaming
- **Status**: `New`
- **Date Introduced**: 2026-09-22
- **Last Reviewed**: —
- **Notes**: Target senior interview question. Requires moving beyond buzzwords to architectural mechanics.

---

### Q03: Boundary Enforcement in a Modular Monolith
- **Question**: In a Modular Monolith sharing a single physical PostgreSQL database, how do we strictly enforce bounded context boundaries so that Module A cannot query or join tables belonging to Module B? What are the architectural trade-offs between separate schemas, separate connection strings, and automated architectural unit tests (e.g., NetArchTest)?
- **Topic**: Software Architecture & Domain-Driven Design
- **Status**: `New`
- **Date Introduced**: 2026-09-22
- **Last Reviewed**: —
- **Notes**: Critical to avoid the "Distributed Monolith" or "Big Ball of Mud" anti-patterns.

---

### Q04: Idempotency under Consumer Crash and Message Re-delivery
- **Question**: RabbitMQ guarantees at-least-once delivery, meaning consumers will inevitably receive duplicate messages (e.g., if a consumer crashes right after processing a message but before acknowledging the broker). How do we design an idempotent consumer in .NET that guarantees business side-effects (like account balance debit) happen exactly once logically without relying on distributed locks?
- **Topic**: Messaging Reliability & Concurrency
- **Status**: `New`
- **Date Introduced**: 2026-09-22
- **Last Reviewed**: —
- **Notes**: Tests understanding of inbox tables, idempotency keys, natural vs. surrogate uniqueness, and database transaction scopes.

---

### Q05: Cache Invalidation & Cache Stampede in High-Frequency Reads
- **Question**: In high-frequency quotation lookups cached in Redis, what happens during a "Cache Stampede" (Thundering Herd) when a hot cache key expires while thousands of concurrent requests arrive? How do we mitigate this using probabilistic early expiration (XFetch) or distributed mutex locking, and what are the trade-offs of each?
- **Topic**: Performance & Distributed Caching
- **Status**: `New`
- **Date Introduced**: 2026-09-22
- **Last Reviewed**: —
- **Notes**: Evaluates production-level performance tuning and database protection strategies.

---

### Q06: Token Revocation & Security Boundaries with JWTs
- **Question**: JSON Web Tokens (JWTs) are stateless and self-contained. If a user's permissions are revoked or their account is compromised, why is invalidating a JWT before its expiration notoriously difficult? What architectural trade-offs exist between short-lived tokens with refresh tokens, token blocklists in Redis, and Reference Tokens (introspection)?
- **Topic**: Identity, Authentication & Security
- **Status**: `New`
- **Date Introduced**: 2026-09-22
- **Last Reviewed**: —
- **Notes**: Core security architecture dilemma balancing zero-database validation latency against real-time revocation capabilities.

---

## Section B: Testing Strategy, Quality & Verification Dilemmas

### Q07: Unit Test Scope & When Mocking Becomes Harmful
- **Question**: What belongs in a unit test, and at what point does mocking become detrimental to software quality? Specifically, why is mocking entity frameworks, repositories, or domain entities an anti-pattern that creates brittle tests, and how do we design pure domain models that can be tested deterministically without mocks?
- **Topic**: Testing Strategy — Unit Testing & Mocking
- **Status**: `New`
- **Date Introduced**: 2026-09-22
- **Last Reviewed**: —
- **Notes**: Distinguishes testing observable behavior from asserting mock call counts.

---

### Q08: In-Memory Databases vs. Testcontainers for Integration Tests
- **Question**: Why do in-memory database providers (like EF Core InMemory or SQLite in-memory) provide false confidence when testing PostgreSQL-backed systems? What specific SQL dialect features, transaction isolation levels, concurrency conflicts, and constraints do in-memory databases fail to replicate, and why is Testcontainers the production standard despite slightly slower boot times?
- **Topic**: Testing Strategy — Integration Testing & Testcontainers
- **Status**: `New`
- **Date Introduced**: 2026-09-22
- **Last Reviewed**: —
- **Notes**: Focuses on realistic infrastructure dependencies vs. misleading speed optimizations.

---

### Q09: Testing Asynchronous Eventual Consistency without Flakiness
- **Question**: In an event-driven flow (e.g., placing an order publishes an event that asynchronously debits an account in another context), how do we write automated tests verifying that the balance was updated without using non-deterministic `Thread.Sleep()` or `Task.Delay()` calls? What polling assertion patterns or test harnesses guarantee fast, reliable verification?
- **Topic**: Testing Strategy — Messaging & Eventual Consistency
- **Status**: `New`
- **Date Introduced**: 2026-09-22
- **Last Reviewed**: —
- **Notes**: Tests understanding of deterministic async testing, timeouts, and polling assertions.

---

### Q10: Testing Retry Policies and Dead-Letter Queues (DLQ)
- **Question**: How do we systematically test that a transient failure triggers our retry policy (e.g., exponential backoff) whereas a permanent poison message (e.g., malformed payload or unrecoverable domain violation) is immediately sent to the Dead-Letter Queue without blocking the consumer partition?
- **Topic**: Testing Strategy — Messaging & Resilience Testing
- **Status**: `New`
- **Date Introduced**: 2026-09-22
- **Last Reviewed**: —
- **Notes**: Critical resilience test verification for asynchronous consumer pipelines.

---

### Q11: What Belongs in an End-to-End (E2E) Test vs. Lower Test Levels?
- **Question**: Why is building an "ice cream cone" testing suite (where most testing is done via browser E2E tests) dangerous for long-term velocity and CI stability? What criteria should a Lead Architect use to decide whether a feature needs a Playwright E2E test versus an API integration test or a unit test?
- **Topic**: Testing Strategy — E2E Testing & Playwright
- **Status**: `New`
- **Date Introduced**: 2026-09-22
- **Last Reviewed**: —
- **Notes**: Balances realism against test maintenance drag, execution time, and flakiness.

---

### Q12: Automated Architectural Boundary Enforcement
- **Question**: How can automated architecture tests (such as NetArchTest) be integrated into a CI/CD pipeline to mathematically guarantee that domain models remain pure (no dependencies on ASP.NET Core, EF Core, or external libraries) and that modules in a Modular Monolith cannot access another module's internal types?
- **Topic**: Testing Strategy — Architecture Testing
- **Status**: `New`
- **Date Introduced**: 2026-09-22
- **Last Reviewed**: —
- **Notes**: Essential pattern for preventing architecture erosion over months and years of development.
