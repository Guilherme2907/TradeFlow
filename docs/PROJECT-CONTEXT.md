# Project Context — TradeFlow

This document captures the real-time architectural baseline, active modules, technological footprint, testing baseline, and technical debt of the repository. It is continuously updated across sessions.

---

## 1. Project Status Overview

| Attribute | Current Value |
| :--- | :--- |
| **Current Stage** | `BOOTSTRAP` |
| **Application Status** | **Not yet created** (Intentional constraint: Governance and study harness established first) |
| **Active Architecture**| None (Pre-implementation phase) |
| **Primary Codebase** | None (Documentation and AI Agent rules only) |
| **Active Testing Baseline** | None (Test strategies formulated; execution begins in Phase 1) |
| **Repository Root** | `g:\Estudo\FullStack-Engineer-Plan\TradeFlow` |
| **Last Updated** | 2026-09-22 |

> [!IMPORTANT]
> The TradeFlow application source code has **not** been scaffolded or implemented yet. The repository is strictly in the **BOOTSTRAP** phase. No source code, project files, test projects, or containers should be generated until initial domain modeling and architectural boundaries are deliberated and decided.

---

## 2. Architectural & Testing Footprint

### 2.1 Current Architecture Style
- **Status**: Pre-architectural setup.
- **Planned Target for Stage 1**: Clean, well-structured Monolith baseline implementing core domain invariants, in-process boundaries, and unit/domain test coverage.

### 2.2 Active Modules / Bounded Contexts
- No application modules active yet.

### 2.3 Active Storage & Persistence
- None active. Planned: PostgreSQL for relational persistence, Redis for distributed caching.

### 2.4 Active Messaging & Integration
- None active. Planned: RabbitMQ for asynchronous task processing / Outbox events; Apache Kafka for high-throughput stream ingestion.

### 2.5 Active Infrastructure & DevOps
- None active. Planned: Docker, Kubernetes, Terraform on Azure.

### 2.6 Active Testing Infrastructure
- None active yet. Planned tools:
  - Unit/Domain: xUnit, FluentAssertions, Bogus
  - Integration: Testcontainers for .NET (PostgreSQL, Redis, RabbitMQ, Kafka)
  - Architecture: NetArchTest.Rules
  - E2E: Playwright (.NET or TypeScript)
  - Performance: k6 / NBomber
  - Mocking / Stubs: WireMock.Net, NSubstitute (used sparingly)

---

## 3. Approved Architecture Decision Records (ADRs)

| ADR ID | Title | Status | Date |
| :--- | :--- | :--- | :--- |
| — | *No ADRs approved yet (Stage: BOOTSTRAP)* | — | — |

---

## 4. Known Technical Debt & Intentional Compromises

| Item | Context / Rationale | Target Resolution Stage |
| :--- | :--- | :--- |
| *None* | Fresh repository in BOOTSTRAP stage | — |

---

## 5. Current Focus & Next Milestones

- **Immediate Task**: Complete repository harness improvements (continuous testing curriculum, session initialization, autonomous context reconstruction).
- **Next Planned Learning Step**: Initiate Domain Exploration and Business Problem formulation for TradeFlow's core trading domain (defining bounded contexts, aggregates, invariants, and initial unit testing boundaries).
