# ADR-0006: Automated Architectural Fitness Functions via NetArchTest

- **Status**: Accepted
- **Date**: 2026-10-06
- **Deciders**: Developer & AI Mentor (Lead Architect)
- **Relevant Issues / Context**: Phase 2 Architectural Governance & Boundary Enforcement

---

## 1. Context & Background
In long-lived enterprise monolithic applications, architectural decay is a pervasive risk. Despite team consensus on Clean Architecture and Domain-Driven Design rules, pressure to deliver features often leads developers to introduce leaky abstractions (e.g., controllers referencing database contexts, domain entities depending on application logic, or handlers triggering other handlers via hidden mediator calls).

Code reviews alone fail to reliably catch these subtle dependency violations over months and across multiple engineers.

## 2. Problem Statement
How do we ensure that architectural boundaries, layer dependencies, and domain encapsulation rules are permanently and automatically enforced as code without relying solely on manual pull request scrutiny?

## 3. Options Considered

### Option A: Manual Code Reviews and Team Linter Rules (EditorConfig)
- Rely on peer code reviews and standard Roslyn analyzers.
- *Pros*: Zero additional test projects or test dependencies.
- *Cons*: Cannot prevent cross-layer assembly reference leaks; human code reviewers frequently miss subtle dependency additions; EditorConfig cannot analyze semantic architectural relationships (e.g., verifying that command handlers do not inject `ISender`).

### Option B: Roslyn Custom Code Analyzers
- Write custom Roslyn compiler analyzers to check AST syntax nodes at compile time.
- *Pros*: Real-time IDE squiggles and build-time enforcement.
- *Cons*: Extremely high development and maintenance overhead; complex Roslyn compiler API; steep learning curve for the engineering team.

### Option C: Automated Architectural Fitness Functions using NetArchTest.Rules (Selected)
- Introduce a dedicated architecture testing project (`tests/TradeFlow.ArchitectureTests`) utilizing **`NetArchTest.Rules`**.
- Express architectural rules as standard xUnit test assertions executed on every local build and CI/CD pull request pipeline.
- *Pros*:
  - **Zero Maintenance Overhead**: Written in standard, expressive, fluent C# assertions.
  - **Instant Execution**: Analyzes compiled assembly metadata via Mono.Cecil in milliseconds (~100–500ms).
  - **Hard CI/CD Gate**: Build fails immediately if any engineer introduces an unauthorized dependency or violates domain encapsulation.
  - **Living Documentation**: The test suite acts as verifiable, executable architecture documentation that cannot drift from reality.
- *Cons*:
  - Requires maintaining an additional test project and assembly references.

## 4. Decision
We select **Option C: Automated Architectural Fitness Functions using NetArchTest.Rules**.

We establish `tests/TradeFlow.ArchitectureTests` to continuously enforce:
1. **Clean Architecture Inward Dependencies**:
   - `TradeFlow.Domain` has zero dependencies on other layers or external infrastructure.
   - `TradeFlow.Application` depends only on `Domain`, never on `Infrastructure` or `Api`.
   - `TradeFlow.Infrastructure` depends on `Application` and `Domain`, but never on `Api`.
   - `TradeFlow.Api.Endpoints` never directly references `Infrastructure` types or DbContexts.
2. **Domain Encapsulation**:
   - All domain entity properties must not have public setters.
   - Domain events must implement `IDomainEvent` and be sealed.
   - Domain entities must have parameterless constructors (private/protected) for EF Core rehydration.
3. **Application Design & Anti-Backdoor Rule ([ADR-0003](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0003-in-process-mediator-and-pipeline-behaviors.md))**:
   - Commands must be sealed and end with `Command`.
   - Command handlers must be sealed and end with `Handler`.
   - Command handlers must never inject `ISender` or `IMediator`, preventing hidden circular execution.

## 5. Trade-offs & Consequences

### Positive Consequences (Gains)
- Guaranteed architectural integrity across developer onboarding, team growth, and rapid delivery.
- Immediate feedback in test runner when a boundary is breached before PR approval.
- Sets the foundation for enforcing bounded-context module boundaries (e.g. `Orders` vs. `Inventory`) in Phase 2.

### Negative Consequences (Costs / Liabilities)
- Architecture tests must be kept up to date when new architectural patterns or modules are intentionally introduced.

## 6. Conditions for Revisiting
This decision will be expanded when new modules (`Inventory`, `Catalog`) are introduced to enforce intra-monolith module boundaries.
