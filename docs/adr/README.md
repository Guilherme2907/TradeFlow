# Architecture Decision Records (ADRs)

This directory houses the formal **Architecture Decision Records (ADRs)** for the TradeFlow system. ADRs capture significant architectural decisions along with their context, evaluated alternatives, trade-offs, and consequences.

---

## 1. Purpose of ADRs

Software architecture is not a collection of best practices; it is the deliberate management of **trade-offs**. Code shows *how* a system is implemented; ADRs preserve *why* it was designed that way and what was consciously sacrificed.

ADRs preserve architectural continuity across study sessions, prevent circular debates, and prevent regression of intentional decisions.

---

## 2. When to Create an ADR

Create an ADR when a decision:
- Impacts system boundaries (e.g., choosing a Modular Monolith vs. microservices).
- Dictates cross-cutting communication patterns (e.g., in-memory events vs. RabbitMQ vs. gRPC).
- Establishes persistence or data consistency strategies (e.g., ACID single-database transactions vs. Transactional Outbox vs. Event Sourcing).
- Introduces external infrastructure or runtime dependencies (e.g., Redis, Kafka, Keycloak).
- Defines security, authentication, or authorization protocols (e.g., JWT vs. Reference Tokens vs. BFF cookie pattern).
- Is **costly or difficult to reverse** later.

---

## 3. When NOT to Create an ADR

Do **not** create ADRs for:
- Routine implementation details or local refactorings.
- Standard framework idiomatic choices (e.g., using `appsettings.json` for configuration).
- Minor code styling, naming conventions, or folder layout inside a module.
- Routine bug fixes or isolated unit test strategies.

---

## 4. ADR Lifecycle & Statuses

- `Proposed`: Formulated and under architectural debate.
- `Accepted`: Formally decided and actively guiding implementation.
- `Superseded`: Replaced by a newer decision (must reference the superseding ADR).
- `Deprecated`: Abandoned because the underlying problem or technology is no longer relevant.

---

## 5. Standard ADR Template

Every new ADR must follow this Markdown structure:

```markdown
# ADR-XXXX: [Short Descriptive Title]

- **Status**: [Proposed | Accepted | Superseded by ADR-YYYY | Deprecated]
- **Date**: YYYY-MM-DD
- **Deciders**: [Architect / Developer / Mentor]
- **Relevant Issues / Context**: [Link or description]

---

## 1. Context & Background
What business or engineering forces are at play? What is the current state of the system?

## 2. Problem Statement
What specific engineering problem or constraint must be resolved? Why can we not continue with the existing approach?

## 3. Options Considered
Detail at least 2–3 viable alternatives:
- **Option A: [Name]**
  - *Pros*: ...
  - *Cons*: ...
- **Option B: [Name]**
  - *Pros*: ...
  - *Cons*: ...

## 4. Decision
What option was selected and why? What is the core rationale?

## 5. Trade-offs & Consequences
- **Positive Consequences (Gains)**: What benefits or guarantees are unlocked?
- **Negative Consequences (Costs / Liabilities)**: What friction, operational complexity, or performance penalties are accepted?

## 6. Conditions for Revisiting
Under what specific circumstances or metrics would this decision stop being appropriate? (e.g., traffic reaching 50k requests/sec, team size growing beyond 20 engineers, latency SLAs tightening below 10ms).
```

---

## 6. ADR Index Registry

| ADR ID | Title | Status | Date | Superceded By |
| :--- | :--- | :--- | :--- | :--- |
| [ADR-0001](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0001-orderline-snapshot-and-identifier-reference.md) | OrderLine Snapshot and Identifier Reference Across Bounded Contexts | Accepted | 2026-09-28 | — |
| [ADR-0002](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0002-monolith-order-intake-transaction-boundary.md) | Monolith Order Intake Transaction Boundary & Aggregate Isolation | Accepted | 2026-09-28 | — |
| [ADR-0003](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0003-in-process-mediator-and-pipeline-behaviors.md) | In-Process Application Dispatching via MediatR and Pipeline Behaviors | Accepted | 2026-09-29 | — |
| [ADR-0004](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0004-persistence-mapping-complex-types-backing-fields.md) | Persistence Mapping via EF Core Complex Types and Backing Fields | Accepted | 2026-10-01 | — |
| [ADR-0005](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0005-minimal-apis-and-problem-details.md) | Presentation Layer via Minimal APIs and RFC 7807 Problem Details | Accepted | 2026-10-04 | — |
| [ADR-0006](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0006-architectural-fitness-functions-and-rules.md) | Automated Architectural Fitness Functions via NetArchTest | Accepted | 2026-10-06 | — |
| [ADR-0007](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0007-modular-monolith-schema-isolation-and-module-contracts.md) | Modular Monolith PostgreSQL Schema Isolation and Module Contracts | Accepted | 2026-10-06 | — |
| [ADR-0008](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0008-modular-infrastructure-and-dedicated-dbcontexts.md) | Modular Infrastructure Organization and Dedicated DbContexts | Accepted | 2026-10-07 | — |
