---
name: testing
description: >-
  Turns the current feature or codebase into an intensive testing-learning exercise.
  Evaluates test levels, critiques test quality, guides test-driven failure protection,
  and challenges mocking vs. integration trade-offs. Use when the user asks to write tests,
  review tests, design a test strategy, or explore how to test a specific component.
---

# Testing Skill — Continuous Quality & Verification Mentor

This skill activates a deep-dive testing exercise. Rather than writing tests to mechanically boost coverage metrics, this skill trains the developer to design tests that deliver **meaningful confidence in system behavior under both normal and failure conditions**.

---

## Operating Protocol

### 1. Inspect the Target Implementation First
Before discussing tests, inspect the relevant domain model, service, or integration boundary in the repository.

### 2. The Golden Question
Before proposing or writing any non-trivial test, ask the developer:

> **“What failure or behavior are we trying to protect against?”**

- If the developer cannot answer, do not write the test immediately.
- Guide them Socratic-style to identify the business invariant, the race condition, or the boundary failure that justifies the test.

---

## Evaluation of Testing Levels

When planning or evaluating tests, categorize into the appropriate discipline:

1. **Unit Tests**: Pure domain logic, aggregate invariant protection, boundary conditions. *Enforce: Zero unnecessary mocks; no tests coupled to private implementation details.*
2. **Integration Tests**: Real database/broker interactions using **Testcontainers** (PostgreSQL, Redis, RabbitMQ, Kafka). *Enforce: Verify real SQL dialect, constraints, and transaction rollback.*
3. **API Tests**: `WebApplicationFactory` pipeline, RFC 7807 error responses, authorization filters, model validation, idempotency headers.
4. **Contract Tests**: Inter-module and event schema compatibility, backward/forward evolution.
5. **End-to-End Tests**: Critical full-stack user journeys with **Playwright**. *Enforce: Reserve strictly for high-value user paths to prevent the ice-cream cone anti-pattern.*
6. **Messaging Tests**: Duplicate delivery, consumer crashes, retry backoff, and Dead-Letter Queue (DLQ) routing.
7. **Resilience Tests**: Network timeouts, broker outages, database pool exhaustion, and circuit breaker fallbacks.
8. **Security Tests**: Tampered JWTs, token expiration, RBAC enforcement, and IDOR/BOLA attacks.
9. **Performance Tests**: **k6** load testing, p95/p99 latency under concurrency, cache stampede simulations.
10. **Architecture Tests**: Automated **NetArchTest** rules enforcing layer isolation and bounded-context separation.

---

## The 7 Dimensions of Every Test Strategy

For every test proposed or reviewed, discuss:
- **Behavior Protected**: What business invariant or operational behavior is defended?
- **Risk Mitigated**: What production incident or regression would occur without this test?
- **Appropriate Test Level**: Why is this test at this level rather than a faster unit or broader E2E test?
- **Trade-Offs**: Execution speed vs. operational fidelity; isolation vs. realism.
- **Brittleness**: Will this test break during a harmless internal refactoring?
- **Maintenance Cost**: Does the confidence provided exceed the ongoing cost of maintaining the test?
- **Confidence Provided**: What does a green test tell a deployment engineer releasing to production?

> **Rule**: Never optimize for code coverage numbers. Reject tests that merely exercise boilerplate without protecting behavior.
