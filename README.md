# TradeFlow — AI-Assisted Senior Software Engineering Laboratory

**TradeFlow** is a hands-on, persistent learning environment designed to elevate an experienced software engineer into **Senior / Staff / Solutions Architect** engineering capabilities.

Instead of generic programming tutorials or greenfield toy projects, this repository serves as the construction site for a complex, production-like financial and trade management platform built with **.NET 8/9, PostgreSQL, Redis, RabbitMQ, Kafka, React, Docker, Kubernetes, and Azure**.

---

## 1. How This Learning Workflow Works

This project operates on an intentional **Code-Reading-First, Testing-Driven & AI-Mentored Engineering Model**:

```
Business Problem
  → Architectural & Testing Inquiry
    → Developer Hypotheses & Trade-off Analysis
      → Architectural / Testing Decision (Recorded in ADR if significant)
        → AI Generates Production-Grade Implementation & Test Scaffolding
          → Rigorous Code & Test Quality Review (Protecting Behavior, Not Line Coverage)
            → Intentional Failure Injection & Chaos Drill
              → Senior Conceptual Quiz
                → Persistent Learning State & Session Checkpoint Update
```

### Roles & Responsibilities
- **The AI Mentor**:
  - Acts as Senior Architect, Code Reviewer, Technical Mentor, SRE, and Staff Interviewer.
  - Eliminates repetitive boilerplate by generating production-ready implementations and test scaffolding.
  - Probes assumptions, challenges naive solutions, injects failures, and evaluates trade-offs.
  - Enforces persistent project context and repository rules.
- **The Developer**:
  - Leads architectural and testing decision-making.
  - Reviews generated code and tests for abstraction leaks, brittleness, and tight coupling.
  - Explains system mechanics and trade-offs in their own words.
  - Triages injected failures and production incidents.
  - Explains architectural choices in professional English.

---

## 2. Session Workflow & Short Prompts

The initial setup was a **one-time bootstrap instruction**. Future study sessions do **NOT** require re-pasting large initialization prompts or repeating context.

The AI mentor autonomously reconstructs full context by reading repository governance files and provides a concise **Context Check** at the start of each session.

You can start any session with concise prompts such as:
- *"Start today's study session."*
- *"Continue from the current project state."*
- *"Let's continue where we stopped."*
- *"Review what we learned last session."*
- *"Interview me about the current architecture."*

---

## 3. Testing as a Continuous Learning Axis

Testing is **not** an isolated module or an afterthought. Every feature explores:
- What should be tested vs. what should NOT be tested.
- Choosing the right test level:
  1. **Unit Tests**: Domain invariants, pure business logic, value objects.
  2. **Integration Tests**: Real dependencies via **Testcontainers** (PostgreSQL, Redis, RabbitMQ, Kafka).
  3. **API Tests**: Contracts, RFC 7807 Problem Details, filters, validation.
  4. **Contract Tests**: Schema evolution and consumer/provider compatibility.
  5. **End-to-End Tests**: Critical full-stack user journeys with **Playwright**.
  6. **Messaging Tests**: Duplicate delivery, idempotency, retry policies, and DLQ handling.
  7. **Resilience Tests**: Outage simulations, timeouts, and fallbacks.
  8. **Security Tests**: JWT tampering, token expiry, RBAC, and IDOR defenses.
  9. **Performance Tests**: Concurrency profiling and latency with **k6**.
  10. **Architecture Tests**: Automated boundary enforcement with **NetArchTest**.
- **Test Quality Review Protocol**: Every test is reviewed against 9 critical questions to prevent brittle assertions and meaningless code coverage padding.

---

## 4. Repository Structure

```
TradeFlow/
├── AGENTS.md                         # Root contract & governing principles for AI agents
├── README.md                         # This file: project overview and learning workflow
├── .agents/
│   └── rules/
│       └── study-harness.md          # Comprehensive AI mentor behavioral protocol & Socratic loop
└── docs/
    ├── PROJECT-CHARTER.md            # Vision, target seniority, core technologies, and philosophy
    ├── PROJECT-CONTEXT.md            # Real-time architectural baseline, active modules, and debt
    ├── LEARNING-STATE.md             # Mastery tracker, diagnostic logs, and 10 testing disciplines
    ├── ROADMAP.md                    # Evolutionary milestone roadmap (Phase 0 to Phase 9)
    ├── OPEN-QUESTIONS.md             # Registry of architectural & testing dilemmas and statuses
    ├── adr/
    │   └── README.md                 # ADR instructions, lifecycle, index, and template
    └── sessions/
        └── README.md                 # Session checkpoint template and archive index
```

---

## 5. Current Status

| Stage | Status |
| :--- | :--- |
| **Stage 0: Repository Bootstrap & Harness Improvement** | **COMPLETED** |
| **Stage 1: Core Domain & Monolith Baseline** | **PENDING (Awaiting Domain Kickoff)** |

> [!NOTE]
> The repository is currently in the **BOOTSTRAP** stage. Application source code, project files, and infrastructure scripts have **not** been created yet. Implementation will commence only after initial domain boundaries and architectural foundations are discussed.
