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
    - Comprehensive testing strategies (Unit, Testcontainers, Contract, E2E, Messaging, Chaos)
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
| **Domain-Driven Design (Aggregates & Invariants)** | `Not Started` | — | Planned for Phase 1 |
| **Modular Monolith Boundary Enforcement** | `Not Started` | — | Planned for Phase 2 |
| **Transactional Outbox Pattern** | `Not Started` | — | Planned for Phase 3 |
| **At-Least-Once Delivery & Idempotent Consumers** | `Not Started` | — | Planned for Phase 3 |
| **RabbitMQ Exchange/Queue Architecture & DLQs** | `Not Started` | — | Planned for Phase 3 |
| **Apache Kafka Partitioning & Offset Management**| `Not Started` | — | Planned for Phase 4 |
| **OAuth2 / OpenID Connect Token Lifecycle & SSO** | `Not Started` | — | Planned for Phase 5 |
| **Distributed Caching, Stampede & Cache-Aside** | `Not Started` | — | Planned for Phase 7 |
| **Database Concurrency, MVCC & Lock Contention**| `Not Started` | — | Planned for Phase 1 & 7 |
| **Distributed Tracing & OpenTelemetry** | `Not Started` | — | Planned for Phase 7 |
| **Kubernetes Core Constructs (Deployments, Ingress)**| `Not Started` | — | Planned for Phase 8 |
| **Infrastructure as Code with Terraform** | `Not Started` | — | Planned for Phase 8 |

---

## 3. Testing & Quality Assurance Mastery Matrix

Testing is a primary engineering axis. Each level is tracked independently to ensure the developer masters appropriate test selection, isolation, trade-offs, and failure verification.

| Testing Discipline | Status | Studied? | Weak Areas / Gaps | Common Mistakes Logged | Interview Questions Practiced | Last Assessed |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **1. Unit Testing** | `Not Started` | No (Phase 1) | — | — | — | — |
| **2. Integration Testing** | `Not Started` | No (Phase 1–2) | — | — | — | — |
| **3. API Testing** | `Not Started` | No (Phase 1) | — | — | — | — |
| **4. Contract Testing** | `Not Started` | No (Phase 3–4) | — | — | — | — |
| **5. End-to-End (E2E) Testing** | `Not Started` | No (Phase 6) | — | — | — | — |
| **6. Messaging Testing** | `Not Started` | No (Phase 3–4) | — | — | — | — |
| **7. Resilience Testing** | `Not Started` | No (Phase 3, 7) | — | — | — | — |
| **8. Security Testing** | `Not Started` | No (Phase 5) | — | — | — | — |
| **9. Performance Testing** | `Not Started` | No (Phase 7) | — | — | — | — |
| **10. Architecture Testing** | `Not Started` | No (Phase 2) | — | — | — | — |

*Evaluation Criteria per Testing Level*:
- **Unit**: Business rules, invariants, value objects, avoiding mocks where fakes/real state work, deterministic AAA.
- **Integration**: Real PostgreSQL & Redis via Testcontainers, EF Core query behavior, transaction rollbacks.
- **API**: HTTP pipeline, RFC 7807 error contracts, authorization filters, validation, idempotency keys.
- **Contract**: Consumer/provider contracts, event schema evolution, backward compatibility.
- **E2E**: Playwright full-stack flows (React + .NET), critical user journeys, minimizing flakiness.
- **Messaging**: RabbitMQ/Kafka consumer tests, duplicate delivery, consumer crashes, DLQ routing.
- **Resilience**: Simulating network timeouts, broker disconnections, database failovers, circuit breakers.
- **Security**: JWT tampering, token expiration, RBAC enforcement, IDOR vulnerabilities.
- **Performance**: k6 load scripts, p99 latency verification, concurrency bottlenecks, Redis stampede.
- **Architecture**: NetArchTest rules, layer isolation, bounded context boundaries, forbidden dependencies.

---

## 4. Architectural & Testing Decision Reasoning Log

Tracks whether architectural and testing choices were defended and explained independently by the developer or guided heavily by the AI mentor.

| Decision / Topic | Category (Arch / Test) | Independent Explanation? | Level of AI Assistance | Date |
| :--- | :--- | :--- | :--- | :--- |
| *None yet (Stage: BOOTSTRAP)* | — | — | — | — |

---

## 5. Senior Diagnostic Question Performance

Logs technical and testing questions asked during sessions to gauge retention, reasoning, and depth under pressure.

| Date | Question Asked | Category | Result (`Correct` / `Partial` / `Incorrect`) | Key Takeaway / Conceptual Gap |
| :--- | :--- | :--- | :--- | :--- |
| *None yet* | *Baseline initial state* | — | — | — |

---

## 6. Recurring Patterns, Anti-Patterns & Mistakes

Tracks persistent tendencies, cognitive traps, premature optimizations, or testing anti-patterns that need conscious correction.

| Observed Pattern / Tendency | Domain (Arch / Code / Test) | Impact / Risk | Remediation Strategy |
| :--- | :--- | :--- | :--- |
| *No patterns logged yet* | Repository bootstrap stage | — | Observe during Phase 1 domain and test design |

---

## 7. Areas with Heavy AI Reliance

Identifies specific areas where the developer still leans on the AI mentor to formulate designs, select test levels, or troubleshoot failures:
- *To be tracked as implementation begins.*

---

## 8. Professional English & Technical Interview Readiness

Logs exercises where the developer explained architecture, trade-offs, or testing strategies in English.

| Date | Scenario / Question | Strengths Noted | Communication / Vocabulary Refinements |
| :--- | :--- | :--- | :--- |
| *None yet* | *Awaiting first architectural discussion* | — | — |
