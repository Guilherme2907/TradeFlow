# Learning State — Engineering Mastery Tracker

This document is the persistent registry of technical competencies, architectural reasoning progress, testing mastery, diagnostic performance, and growth areas throughout the TradeFlow project.

---

## 1. Developer Profile & Baseline

- **Current Profile**: Experienced Software Developer with solid practical foundations in .NET, C#, and backend development.
- **Learning Objective**: Transition from mid/senior implementer to **Senior / Staff / Lead Software Engineer & Solutions Architect**.
- **Initial Baseline Assessment**:
  - Confident in day-to-day C# / .NET development and standard backend features.
  - Ready to rigorously study and prove deep mastery in:
    - Distributed systems architecture & trade-offs
    - Advanced Domain-Driven Design (DDD) & transaction boundaries
    - Comprehensive testing strategies (Unit, Testcontainers, Contract, E2E, Messaging, Chaos, Architecture)
    - Asynchronous messaging (RabbitMQ) vs. Event Streaming (Kafka)
    - Resiliency patterns, failure injection, and production triage
    - Identity architecture (OAuth2 / OIDC / SSO)
    - High-concurrency caching & race conditions (Redis, PostgreSQL)
    - Cloud-native infrastructure (Kubernetes, Terraform, Azure)
    - Professional architectural communication in English
- **Mastery Standard**: A concept or test discipline is only recorded as **Mastered / Strong** when the developer can independently explain:
  1. *Why it exists*
  2. *When to use it*
  3. *When NOT to use it*
  4. *Core trade-offs and alternatives*
  5. *Failure modes and operational recovery*

---

## 2. Architectural Mastery Matrix

*Status Categories*:
- `Strong`: Confidently explains why, when, trade-offs, failure modes, and teaches it.
- `Good`: Solid understanding, can evaluate trade-offs with minor prompting.
- `Needs Review`: Grasps theoretical premise; struggles with edge cases or failure behavior.
- `Weak / Misunderstood`: Exhibits misconceptions or anti-patterns under scrutiny.
- `Not Started`: Scheduled for study in future roadmap phases.

| Topic / Concept | Status | Last Assessed | Evidence / Evaluation Notes |
| :--- | :--- | :--- | :--- |
| **Domain-Driven Design (Aggregates & Invariants)** | `Strong` | 2026-10-06 | Implemented `Order` and `InventoryItem` aggregates with encapsulated state, non-public setters, `Money` value object, immutable snapshots, and pure unit tests. |
| **Modular Monolith Boundary Enforcement** | `Strong` | 2026-10-06 | Successfully defended PostgreSQL schema isolation (`orders.*` vs `inventory.*`) and explicit in-process contract facades (`IInventoryModule`), backed by 14 NetArchTest fitness tests. |
| **Transactional Outbox Pattern** | `Strong` | 2026-10-18 | Articulated the Dual-Write problem, 2PC/XA pitfalls, `FOR UPDATE SKIP LOCKED` non-blocking multi-pod polling, and poison message isolation (`RetryCount < 5`) to prevent head-of-line blocking. Implemented `DomainEventsToOutboxMessagesInterceptor`, `OutboxProcessor`, and `OutboxPublisherBackgroundService` verified via concurrent Testcontainers (ADR-0011). |
| **At-Least-Once Delivery & Idempotent Consumers** | `Good` | 2026-10-13 | Identified that at-least-once delivery causes duplicate messages and analyzed consumer deduplication via Inbox table pattern (unique message ID tracking) and semantic state idempotency (ADR-0011). |
| **RabbitMQ Exchange/Queue Architecture & DLQs** | `Strong` | 2026-10-19 | Selected `Topic` exchange for flexible routing (`orders.OrderSubmittedEvent` vs `orders.*`), analyzed Singleton `IConnection` vs Scoped `IChannel` multiplexing, and verified Publisher Confirms + Dead-Letter Queue (`x-dead-letter-exchange`) with `Testcontainers.RabbitMq` (ADR-0011). |
| **Apache Kafka Partitioning & Offset Management**| `Not Started` | — | Planned for Phase 4 |
| **OAuth2 / OpenID Connect Token Lifecycle & SSO** | `Not Started` | — | Planned for Phase 5 |
| **Distributed Caching, Stampede & Cache-Aside** | `Not Started` | — | Planned for Phase 7 |
| **Database Concurrency, MVCC & Lock Contention**| `Strong` | 2026-10-07 | Independently analyzed OCC retry storm risks, evaluated DDD trade-offs of atomic SQL updates, designed Pessimistic Locking with row-level locks, and enforced deterministic ordering to prevent deadlocks (ADR-0009). Verified with high-concurrency Testcontainers. |
| **Generic Cross-Context Unit of Work ($O(1)$ Scaling)** | `Strong` | 2026-10-13 | Designed generic `UnitOfWork` accepting `IEnumerable<IModuleDbContext>` over shared scoped `DbConnection`, decoupling UoW from concrete module names while maintaining schema isolation (ADR-0010). |
| **Fail-Fast Transactions vs. ORM Transparent Retries** | `Strong` | 2026-10-13 | Articulated that transparent ORM retries (`EnableRetryOnFailure`) on write transactions with pessimistic locks cause ghost in-memory mutations and prolonged contention; advocated for fail-fast atomic transactions with resilience delegated to boundary protocols (Idempotency Keys). |
| **Distributed Tracing & OpenTelemetry** | `Not Started` | — | Planned for Phase 7 |
| **Kubernetes Core Constructs (Deployments, Ingress)**| `Not Started` | — | Planned for Phase 8 |
| **Infrastructure as Code with Terraform** | `Not Started` | — | Planned for Phase 8 |

---

## 3. Testing & Quality Assurance Mastery Matrix

Testing is a primary engineering axis. Each level is tracked independently to ensure the developer masters appropriate test selection, isolation, trade-offs, and failure verification.

| Testing Discipline | Status | Studied? | Weak Areas / Gaps | Common Mistakes Logged | Interview Questions Practiced | Last Assessed |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **1. Unit Testing** | `Strong` | Yes (Phase 1–2) | — | Mutating aggregate state before gateway call | Domain invariant protection, pure VO arithmetic, stock allocation invariants | 2026-10-06 |
| **2. Integration Testing** | `Strong` | Yes (Phase 1–2) | — | Relying on EF InMemory / SQLite emulators | In-memory/SQLite vs. Testcontainers PostgreSQL, schema creation with multiple DbContexts | 2026-10-07 |
| **3. API Testing** | `Strong` | Yes (Phase 1–2) | — | Emulator divergence vs production parity | RFC 7807 contracts, HTTP status code assertions, real PostgreSQL cross-context commits | 2026-10-07 |
| **4. Contract Testing** | `Not Started` | No (Phase 3–4) | — | — | — | — |
| **5. End-to-End (E2E) Testing** | `Not Started` | No (Phase 6) | — | — | — | — |
| **6. Messaging Testing** | `Not Started` | No (Phase 3–4) | — | — | — | — |
| **7. Resilience Testing** | `Not Started` | No (Phase 3, 7) | — | — | — | — |
| **8. Security Testing** | `Not Started` | No (Phase 5) | — | — | — | — |
| **9. Performance Testing** | `Not Started` | No (Phase 7) | — | — | — | — |
| **10. Architecture Testing** | `Strong` | Yes (Phase 2) | — | Multi-project explosion vs single-project modularity | NetArchTest layer rules, entity encapsulation, Anti-Backdoor rule, bounded context isolation | 2026-10-06 |

*Active Testing Baseline (88 passing tests)*:
- Unit/Domain: 41 tests (Order lifecycle, Money arithmetic, InventoryItem invariants, AggregateRoot temporal audit tracking)
- Unit/Application: 17 tests (Command handlers, Validation behaviors, InventoryModule atomic reservation, deterministic lock ordering)
- Architecture: 17 tests (Layer dependencies, entity encapsulation, Anti-Backdoor rule, Module boundaries, infrastructure isolation, IModuleDbContext enforcement)
- API Integration: 5 tests (HTTP contracts, RFC 7807 problem details, real PostgreSQL cross-context database commits)
- Infrastructure Integration: 8 tests (Real PostgreSQL in Docker Testcontainers: Order persistence, OCC rowversion concurrency, InventoryItem persistence, hot SKU high-concurrency contention without OCC exceptions, multi-item opposing order deadlock prevention, cross-context atomic commit, business failure rollback, database exception fail-fast rollback)

---

## 4. Architectural & Testing Decision Reasoning Log

Tracks whether architectural and testing choices were defended and explained independently by the developer or guided heavily by the AI mentor.

| Decision / Topic | Category (Arch / Test) | Independent Explanation? | Level of AI Assistance | Date |
| :--- | :--- | :--- | :--- | :--- |
| ADR-0001: OrderLine Identifier Reference & Financial Snapshot | Arch | Yes (on ID reference for modularity) | Moderate (AI guided point-in-time snapshot rationale) | 2026-09-28 |
| ADR-0002: Monolith Order Intake Transaction Boundary | Arch | Yes (identified concurrency locks & dual-write risk) | Low (Developer independently identified core trade-offs) | 2026-09-28 |
| ADR-0003: In-Process Application Dispatching via MediatR | Arch | Yes (defended decoupling & pipeline behaviors) | Low (Developer independently articulated trade-offs and decision) | 2026-09-29 |
| ADR-0004: Persistence Mapping via Complex Types & Backing Fields | Arch | Yes (selected backing field approach after reviewing DDD patterns) | Moderate (AI presented backing field vs owned entity trade-offs) | 2026-09-29 |
| ADR-0005: Feature-Oriented Minimal APIs & Problem Details | Arch | Yes (defended clean syntax, performance, uniform client contract) | Low (Developer independently articulated client value of Problem Details) | 2026-10-04 |
| ADR-0006: Automated Architectural Fitness Functions via NetArchTest | Test / Arch | Yes (identified layer dependency rules & entity encapsulation) | Low (Developer independently articulated Clean Architecture constraints & entity immutability) | 2026-10-06 |
| ADR-0007: Modular Monolith PostgreSQL Schema Isolation & Module Contracts | Arch | Yes (defended schema separation & explicit module contracts over mediator buses) | Low (Developer independently articulated schema boundaries and `IInventoryModule` facade rationale) | 2026-10-06 |
| ADR-0008: Modular Infrastructure Organization & Dedicated DbContexts | Arch / Persistence | Yes (proposed module-based folder structure and separate DbContexts per bounded context) | Low (Developer independently proposed separate DbContexts and module folders) | 2026-10-07 |
| ADR-0009: Pessimistic Row Locking and Deterministic Ordering for Inventory Contention | Arch / Concurrency | Yes (independently evaluated OCC retry storms and selected pessimistic row locking with deterministic sorting) | Low (Developer independently articulated failure modes and chosen strategy) | 2026-10-07 |
| ADR-0010: Generic Unit of Work over Shared Connection and Fail-Fast Transaction Boundaries | Arch / Persistence | Yes (challenged UoW scalability across modules and recognized danger of ORM retries in critical systems) | Low (Developer independently questioned UoW module scalability and advocated removing ORM retry loops) | 2026-10-13 |

---

## 5. Senior Diagnostic Question Performance

Logs technical and testing questions asked during sessions to gauge retention, reasoning, and depth under pressure.

| Date | Question Asked | Category | Result (`Correct` / `Partial` / `Incorrect`) | Key Takeaway / Conceptual Gap |
| :--- | :--- | :--- | :--- | :--- |
| 2026-09-28 | Cross-context coupling & historical financial integrity (OrderLine $\leftrightarrow$ Product) | Arch / DDD | `Partial` | Strong instinct on decoupling via identifiers; initially missed why orders require immutable price/name snapshots to protect historical financial integrity. |
| 2026-09-28 | Monolith transaction boundary & concurrency vs. eventual consistency | Arch / Distributed | `Correct` | Accurately deduced that single transactions cause lock contention and that eventual consistency without an outbox risks dual-write data loss. |
| 2026-09-29 | OrderLine accumulation logic (ProductId + UnitPrice) | Domain / B2B | `Correct` | Inquired about seller model and deduced tiered/promotional price discrimination scenarios on the same SKU. |
| 2026-09-29 | In-memory aggregate limits vs. concurrency control | Arch / Persistence | `Correct` | Correctly noted that in-memory domain models cannot resolve race conditions alone; identified database row versioning / optimistic concurrency. |
| 2026-10-04 | Minimal APIs vs Controllers (Performance, testability, filter pipelines) | Arch / API | `Good` | Correctly identified chaining syntax and performance edge; clarified testability equivalence in WebApplicationFactory. |
| 2026-10-04 | Problem Details RFC 7807 for API error contracts | Arch / API | `Correct` | Clearly articulated client-side consistency and predictable error schemas. |
| 2026-10-06 | Clean Architecture layer dependency rules & entity encapsulation | Arch / Testing | `Correct` | Accurately identified inward dependency flow, non-public entity setters, and read-only collection exposure. |
| 2026-10-06 | Multi-project explosion vs. Modular Monolith compilation & encapsulation trade-offs | Arch / Modularity | `Good` | Initially favored multi-project per module; reasoned through C# internal leakage and MSBuild build time trade-offs after analysis. |
| 2026-10-06 | Database schema isolation vs. flat schema in Modular Monolith | Arch / Persistence | `Correct` | Strongly advocated for PostgreSQL schemas (`orders.*`, `inventory.*`) to prevent cross-context joins and establish hard data boundaries. |
| 2026-10-06 | In-process module communication (Explicit Interface vs. MediatR Command Bus) | Arch / Design | `Correct` | Correctly identified that explicit facades (`IInventoryModule`) provide compile-time discoverability and clear contracts without hiding dependencies behind mediator. |
| 2026-10-07 | Ports & Adapters for Monolith-to-Microservices Evolution | Arch / Design | `Correct` | Precisely articulated that inverting gateway dependencies allows the Infrastructure adapter to swap from in-process to HTTP/messaging with zero impact on the Application layer. |
| 2026-10-07 | Dedicated DbContexts & Modular Infrastructure Organization | Arch / Persistence | `Correct` | Proposed dedicated DbContexts (`OrdersDbContext`, `InventoryDbContext`) to eliminate cross-module LINQ joins and group infrastructure concerns by bounded context. |
| 2026-10-07 | Relational OCC vs. DDD Aggregate Child Mutations (PostgreSQL xmin) | Arch / Concurrency | `Correct` | Identified that child table inserts do not bump parent table xmin in relational databases; resolved by touching parent aggregate state (`UpdatedAt`) to guarantee concurrency conflicts. |
| 2026-10-07 | Aggregate Root Temporal Encapsulation & Testcontainers Parity over In-Memory Emulators | Arch / Testing | `Correct` | Correctly identified that temporal audit properties (`CreatedAt`, `UpdatedAt`) belong in `AggregateRoot` to enforce uniform consistency boundaries, and challenged SQLite in-memory emulators to ensure 100% production database fidelity in API integration tests. |
| 2026-10-07 | Pessimistic Locking vs. OCC Retry Storms & Deterministic Deadlock Prevention | Arch / Concurrency | `Correct` | Independently analyzed why OCC creates retry storms on hot items, rejected atomic SQL due to DDD leakage, selected pessimistic row locking (`SELECT FOR UPDATE`), and identified deadlock risks solved by deterministic sorting. |
| 2026-10-13 | Multi-context UnitOfWork scalability & why transparent ORM retries are dangerous | Arch / Persistence | `Correct` | Challenged hardcoded DbContext parameters in `UnitOfWork` as unscalable and independently reasoned that transparent ORM retries are risky for critical production transactions compared to fail-fast boundaries with edge idempotency. |

---

## 6. Recurring Patterns, Anti-Patterns & Mistakes

Tracks persistent tendencies, cognitive traps, premature optimizations, or testing anti-patterns that need conscious correction.

| Observed Pattern / Tendency | Domain (Arch / Code / Test) | Impact / Risk | Remediation Strategy |
| :--- | :--- | :--- | :--- |
| Overlooking point-in-time immutability in commercial contracts | Domain / DDD | Historical financial corruption if catalog updates | Reinforce immutable snapshots vs live entity references across bounded contexts |
| Misplacing module facades in Infrastructure rather than Application | Arch / Layering | Inadvertent dependency inverted or test assembly reference errors | Reiterate that module facades coordinating repositories are Application use cases, not Infrastructure adapters |

---

## 7. Areas with Heavy AI Reliance

Identifies specific areas where the developer still leans on the AI mentor to formulate designs, select test levels, or troubleshoot failures:
- Domain modeling of immutable financial contracts (snapshots vs live queries).
- Recognizing when a module facade belongs to the Application layer rather than Infrastructure.

---

## 8. Professional English & Technical Interview Readiness

Logs exercises where the developer explained architecture, trade-offs, or testing strategies in English.

| Date | Scenario / Question | Strengths Noted | Communication / Vocabulary Refinements |
| :--- | :--- | :--- | :--- |
| 2026-09-28 | Explaining cross-module coupling and transaction boundaries | Clear, direct formulation of architectural intuition ("face latency issues", "split into a separate module", "data consistency issue") | Provided interview-ready framing: "immutable financial contract frozen in time", "database lock contention on hot inventory rows", "application-layer transaction orchestration". |
| 2026-09-29 | Clarifying B2B seller model & explaining optimistic concurrency | Proactively clarified business domain constraints before answering; accurate use of architectural terms ("row version", "optimistic concurrency", "application layer"). | Refined terminology: "multi-tenant marketplace vs. centralized distributor", "stateless aggregate instances across HTTP requests", "Optimistic Concurrency Control (OCC)". |
| 2026-09-29 | Defending MediatR vs. Direct Handlers | Fluid, grammatically sound, and precise technical comparison ("promotes complete decoupling", "pipeline behaviors", "vertical slice approach"). | Refined framing: "centralized cross-cutting pipeline", "indirection vs. maintainability trade-off", "anti-backdoor boundary discipline". |
| 2026-10-06 | Explaining database schema isolation and explicit module contracts | Articulated trade-offs with high precision ("stronger boundaries between modules", "explicit interface exposed by the Inventory module", "makes the contract explicit"). | Refined framing: "hard relational isolation at zero operational cost", "preventing leaky joins across bounded contexts", "in-process public API facade pattern". |
