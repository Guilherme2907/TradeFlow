# ADR-0003: In-Process Application Dispatching via MediatR and Pipeline Behaviors

- **Status**: Accepted
- **Date**: 2026-09-29
- **Deciders**: Developer & AI Mentor (Lead Architect)
- **Relevant Issues / Context**: Phase 1 Clean Monolith Application Layer Design

---

## 1. Context & Background
In Clean / Onion Architecture, the Application Layer orchestrates domain logic, coordinates repository persistence, and enforces cross-cutting concerns (validation, logging, telemetry, transactions). 

The presentation layer (ASP.NET Core Controllers or Minimal APIs) must dispatch user intentions to application use cases without becoming coupled to domain or infrastructure implementation details.

## 2. Problem Statement
How should application use cases (commands and queries) be structured and invoked from the API layer?
- Should we use explicit, directly injected command handler interfaces (e.g. `ICommandHandler<TCommand, TResult>`)?
- Or should we adopt the in-process mediator pattern (`MediatR`) utilizing `IPipelineBehavior` for cross-cutting orchestration?

## 3. Options Considered

### Option A: Explicit Direct Handlers with Standard DI
- Every use case defines an explicit handler interface and implementation injected directly into Minimal API endpoints or controllers.
- Cross-cutting concerns are composed via decorators or ASP.NET Core endpoint filters.
- *Pros*:
  - Maximum code navigation clarity: IDE "Go to implementation" goes directly to the handler with zero indirection.
  - Zero third-party runtime mediator dependency.
- *Cons*:
  - Composing multiple cross-cutting behaviors (validation, logging, metrics, transaction management) across dozens of handlers requires repetitive decorator boilerplate or verbose DI registration.
  - Tighter presentation coupling to specific handler signatures.

### Option B: In-Process Mediator via MediatR with Pipeline Behaviors (Selected)
- Use cases are modeled as `IRequest<TResponse>` commands/queries dispatched through `ISender` / `IMediator`.
- Cross-cutting concerns are executed uniformly using generic `IPipelineBehavior<TRequest, TResponse>` (e.g., automated FluentValidation, telemetry, logging).
- *Pros*:
  - Clean, uniform presentation layer: endpoints only depend on `ISender`.
  - Extensible pipeline: adding validation, logging, or metric tracking across all commands requires a single pipeline behavior without modifying handlers.
  - Prepares the application for event-driven patterns and CQRS segregation.
- *Cons*:
  - Indirection: Navigation requires searching for handler implementations rather than direct symbol navigation.
  - Architectural temptation: Risk of using MediatR as a "service locator" or cross-module backdoor if not disciplined.

## 4. Decision
We select **Option B: In-Process Mediator via MediatR with Pipeline Behaviors**, with a strict **Architectural Guardrail**:

> **The Modular Boundary Rule**: MediatR commands and queries are strictly private to their respective module/slice. In Phase 2 (Modular Monolith), Module A is strictly prohibited from sending a MediatR command to Module B. Cross-module communication must use explicit public contracts, domain events, or facade interfaces.

## 5. Trade-offs & Consequences

### Positive Consequences (Gains)
- Standardized command pipeline with automatic validation before reaching domain logic.
- Consistent structured logging and latency telemetry across all application use cases.
- Decoupled API endpoints.

### Negative Consequences (Costs / Liabilities)
- Indirection during code navigation (mitigated by consistent naming conventions: `[Verb][Entity]Command` and `[Verb][Entity]CommandHandler` colocated in the same file or folder).
- Strict discipline required to prevent leaky cross-module invocations in future phases.

## 6. Conditions for Revisiting
This decision will be reviewed if pipeline indirection demonstrably complicates performance profiling, or if .NET native compile-time source generation (e.g., direct typed dispatcher generators) renders runtime reflection-based pipelines obsolete.
