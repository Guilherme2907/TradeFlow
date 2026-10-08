# Session Checkpoint: Slice 1 — B2B Order Intake & Clean Monolith Baseline

- **Date**: 2026-10-04
- **Project Stage**: Phase 1 — Core Domain & Clean Monolith Baseline
- **Focus Area**: End-to-End Implementation of Slice 1 (Domain $\rightarrow$ Application $\rightarrow$ Infrastructure $\rightarrow$ Presentation $\rightarrow$ Test Suites)

---

## 1. Implementation Progress

Completed the full vertical implementation of **Slice 1: B2B Purchase Order Lifecycle** (`Draft` $\rightarrow$ `Submitted` with commercial snapshots, invariant enforcement, stock reservation gateway, and persistence mapping):

1. **Domain Layer** ([`src/TradeFlow.Domain/`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/)):
   - Base abstractions: [`Entity<TId>`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Common/Entity.cs), [`AggregateRoot<TId>`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Common/AggregateRoot.cs), [`IDomainEvent`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Common/IDomainEvent.cs), [`DomainException`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Common/DomainException.cs).
   - Strongly-typed identifiers: `OrderId`, `CustomerId`, `ProductId`, `OrderLineId`.
   - Value Objects: [`Money`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Common/Money.cs) (immutable, banker's rounding `MidpointRounding.ToEven`, currency isolation, arithmetic operators).
   - Entities & Aggregates: [`OrderLine`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Orders/OrderLine.cs) (immutable commercial snapshot of SKU, name, unit price, quantity), [`Order`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Orders/Order.cs) (aggregate root encapsulating `_lines`, status transitions, minimum order value invariant, currency isolation).
   - Domain Events & Exceptions: `OrderCreatedEvent`, `OrderLineAddedEvent`, `OrderSubmittedEvent`, `OrderCancelledEvent`, [`OrderInvariantException`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Orders/Exceptions/OrderInvariantException.cs).

2. **Application Layer** ([`src/TradeFlow.Application/`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/)):
   - MediatR Commands, Handlers, and Validators:
     - [`CreateOrderCommand`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/Orders/Commands/CreateOrder/CreateOrderCommand.cs) + Validator + Handler
     - [`AddOrderLineCommand`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/Orders/Commands/AddOrderLine/AddOrderLineCommand.cs) + Validator + Handler
     - [`SubmitOrderCommand`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/Orders/Commands/SubmitOrder/SubmitOrderCommand.cs) + Handler
   - Pipeline Behaviors: [`LoggingBehavior`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/Common/Behaviors/LoggingBehavior.cs), [`ValidationBehavior`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/Common/Behaviors/ValidationBehavior.cs).
   - Gateway Abstractions: [`IOrderRepository`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/Orders/Gateways/IOrderRepository.cs), [`IUnitOfWork`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/Orders/Gateways/IUnitOfWork.cs), [`IInventoryReservationService`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/Orders/Gateways/IInventoryReservationService.cs).
   - Unified Result Pattern: [`Result<T>`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/Common/Models/Result.cs), [`Error`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/Common/Models/Error.cs).

3. **Infrastructure Layer** ([`src/TradeFlow.Infrastructure/`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/)):
   - Persistence: [`TradeFlowDbContext`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Persistence/TradeFlowDbContext.cs).
   - Entity Configurations: [`OrderConfiguration`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Persistence/Configurations/OrderConfiguration.cs) (mapping `Money` via `ComplexProperty`, backing field `_lines`, row version concurrency), [`OrderLineConfiguration`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Persistence/Configurations/OrderLineConfiguration.cs).
   - Repositories: [`OrderRepository`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Persistence/Repositories/OrderRepository.cs) (eager loading `_lines`).
   - Gateways: [`InMemoryInventoryReservationService`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Services/InMemoryInventoryReservationService.cs).

4. **Presentation Layer (API)** ([`src/TradeFlow.Api/`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Api/)):
   - Feature Minimal APIs: [`OrderEndpoints`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Api/Endpoints/OrderEndpoints.cs) (`POST /api/orders`, `POST /api/orders/{id}/lines`, `POST /api/orders/{id}/submit`).
   - RFC 7807 Error Mapping: [`ResultExtensions.ToHttpResult()`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Api/Common/Errors/ResultExtensions.cs) mapping Application `Error` objects to standardized ProblemDetails responses.

5. **Automated Test Suites**:
   - `TradeFlow.Domain.UnitTests`: 26 pure unit tests (100% green, 0 mocks, ~170ms).
   - `TradeFlow.Application.UnitTests`: 9 unit tests with NSubstitute (100% green, ~430ms).
   - `TradeFlow.Infrastructure.IntegrationTests`: Testcontainers PostgreSQL repository tests.
   - `TradeFlow.Api.IntegrationTests`: 5 WebApplicationFactory contract tests (100% green, ~1s).

---

## 2. Architectural Decisions Made

All decisions were thoroughly debated, analyzed for trade-offs, and recorded in `docs/adr/`:

1. **[ADR-0001](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0001-orderline-snapshot-and-identifier-reference.md)**: *OrderLine Snapshot and Identifier Reference Across Bounded Contexts*.
   - **Decision**: `OrderLine` references `Product` by `ProductId` (no foreign key/navigation property) and stores an immutable snapshot of `Sku`, `ProductName`, and `UnitPrice`.
   - **Rationale**: Guarantees historical financial integrity against retroactive mutation if catalog prices change; maintains bounded context isolation.

2. **[ADR-0002](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0002-monolith-order-intake-transaction-boundary.md)**: *Monolith Order Intake Transaction Boundary & Aggregate Isolation*.
   - **Decision**: Single application-layer transaction orchestrating order submission and synchronous inventory reservation via domain interface, avoiding premature distributed outbox in Phase 1 while isolating aggregate boundaries.

3. **[ADR-0003](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0003-in-process-mediator-and-pipeline-behaviors.md)**: *In-Process Application Dispatching via MediatR and Pipeline Behaviors*.
   - **Decision**: MediatR commands/handlers with strict "Anti-Backdoor" rule: handlers never invoke other handlers; cross-cutting concerns (logging, validation) handled via pipeline behaviors.

4. **[ADR-0004](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0004-persistence-mapping-complex-types-backing-fields.md)**: *Persistence Mapping with EF Core Complex Types and Backing Fields*.
   - **Decision**: Encapsulated aggregate collections mapped to private field `_lines` (`PropertyAccessMode.Field`); Value Object `Money` mapped using EF Core 8+ `ComplexProperty`.

5. **[ADR-0005](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0005-minimal-apis-and-problem-details.md)**: *Feature-Oriented Minimal APIs and RFC 7807 Problem Details*.
   - **Decision**: Endpoint definitions grouped by feature using route groups; unified domain/application error translation to RFC 7807 `application/problem+json`.

---

## 3. Testing Decisions Made

| Dimension | Decision Taken | Rationale & Trade-offs |
| :--- | :--- | :--- |
| **Domain Tests** | Pure Unit Tests (0 mocks, in-memory state) | Value objects and aggregate invariants must execute with zero I/O, deterministic assertions, and sub-millisecond execution. |
| **Application Tests** | Unit Tests with NSubstitute for external gateways | Tested orchestration, pipeline validation, and logging behavior without invoking real database or network services. |
| **Infrastructure Tests** | Testcontainers with real PostgreSQL | Testing real EF Core mappings, PostgreSQL transaction isolation, and column types. In-memory databases do not replicate PostgreSQL behaviors. |
| **API Integration Tests** | `WebApplicationFactory` + SQLite in-memory with real relational schema (`EnsureCreated`) | Tested HTTP routing, JSON payload deserialization, Location headers, and RFC 7807 ProblemDetails serialization without requiring external Docker daemon running. |
| **What Was NOT Tested** | EF Core queries via mocked DbContext | Mocking `DbContext` or `DbSet` provides zero confidence and tests the mock setup rather than database behavior. |

---

## 4. Important Tests Added & What They Protect

### 4.1 Domain Layer (`TradeFlow.Domain.UnitTests`)
- `MoneyTests`: Arithmetic additions, subtractions, currency mismatch protection, banker's rounding (`MidpointRounding.ToEven`).
- `OrderTests.Create_WithValidParameters_ShouldInitializeDraftOrder`: Verifies initial `Draft` status, ID assignment, and `OrderCreatedEvent` emission.
- `OrderTests.Create_WhenMinimumOrderValueCurrencyDiffers_ShouldThrowException`: Protects financial consistency across currency domains.
- `OrderTests.AddLine_WhenSameProductAndPriceAddedTwice_ShouldAccumulateQuantity`: Verifies line item consolidation for identical commercial terms.
- `OrderTests.AddLine_WhenSameProductWithDifferentPriceAdded_ShouldCreateSeparateLines`: Verifies support for tiered/discounted pricing on the same product SKU.
- `OrderTests.Submit_WhenOrderHasNoLines_ShouldThrowException`: Invariant protection preventing submission of empty purchase orders.
- `OrderTests.Submit_WhenTotalBelowMinimumOrderValue_ShouldThrowException`: Invariant protection enforcing commercial contractual minimums.
- `OrderTests.Submit_WhenAlreadySubmitted_ShouldThrowException`: State machine protection against invalid state transitions.

### 4.2 Application Layer (`TradeFlow.Application.UnitTests`)
- `SubmitOrderCommandHandlerTests.Handle_WhenInventoryReservationFails_ShouldReturnFailureAndNotSubmitOrder`: Critical bug prevention: verifies that stock reservation failure does NOT leave the aggregate mutated in memory or committed to the database.
- `ValidationBehaviorTests`: Verifies pipeline short-circuiting on invalid command inputs before reaching handlers.

### 4.3 Presentation Layer (`TradeFlow.Api.IntegrationTests`)
- `CreateOrder_WithValidPayload_ShouldReturnCreatedWithLocationAndId`: Verifies HTTP 201, `Location: /api/orders/{id}`, and JSON Guid body.
- `CreateOrder_WithInvalidCurrency_ShouldReturnBadRequestProblemDetails`: Verifies FluentValidation error translation to RFC 7807 ProblemDetails with HTTP 400.
- `AddOrderLine_WhenOrderDoesNotExist_ShouldReturnNotFoundProblemDetails`: Verifies aggregate not-found mapping to RFC 7807 with HTTP 404 and error code `ORDER_NOT_FOUND`.
- `SubmitOrder_WhenOrderHasNoLines_ShouldReturnBadRequestProblemDetails`: Verifies domain invariant failure translation to RFC 7807 with HTTP 400 and code `ORDER_NO_LINES`.
- `FullOrderLifecycle_ShouldCompleteSuccessfully`: Full end-to-end integration test creating an order, adding lines, and submitting successfully (HTTP 200).

---

## 5. What Was Understood Well

- **Identifier Decoupling across Bounded Contexts**: Independent deduction that referencing products only by ID avoids coupling services and facilitates future microservice extraction.
- **Lock Contention & Dual-Write Dilemma**: Solid identification that a monolithic single transaction causes database lock contention under load, while eventual consistency without an Outbox risks inconsistent states.
- **B2B Multi-Priced Line Items**: Excellent domain reasoning inquiring about seller structures and identifying why orders can hold the same SKU at different prices (promotional/tiered pricing).
- **Optimistic Concurrency Control**: Clear grasp that domain aggregates in memory cannot solve race conditions independently and require database concurrency tokens / row versions.
- **Problem Details RFC 7807**: Strong articulation of how standard error payloads benefit API clients by providing uniform error structures.

---

## 6. What Was Struggled With / Conceptual Gaps

- **Historical Financial Immutability vs. Live Catalog References**: Initial inclination to view `OrderLine` merely as a relational pointer to catalog data. Mastered why purchase orders are legal/financial contracts frozen in time that must snapshot SKU, name, and unit price.
- **State Mutation Prior to External Gateway Confirmation**: In `SubmitOrderCommandHandler`, initial logic mutated aggregate state in memory (`order.Submit()`) before verifying external stock reservation. Addressed by introducing explicit pre-validation (`order.ValidateCanSubmit()`), executing the external call, and mutating aggregate state only upon confirmation.
- **EF Core InMemory Limitations with DDD Complex Types**: Experienced firsthand how EF Core's InMemory provider fails with nullable complex types (`Money?`), reinforcing Open Question Q08.
- **Minimal APIs vs. Controllers**: Clarified differences in testability (`WebApplicationFactory` works identically for both) and design trade-offs (Minimal APIs eliminate heavy MVC action filter overhead and map cleanly to vertical slices).

---

## 7. Important Technical Concepts Explored

- **DDD Aggregate Root Invariants & Private Backing Fields**: Encapsulating collections via `List<OrderLine> _lines` exposed as `IReadOnlyCollection<OrderLine>`, mapped via EF Core `PropertyAccessMode.Field`.
- **Value Object Immutability & Banker's Rounding**: `Money` arithmetic using `MidpointRounding.ToEven` to avoid statistical rounding drift in commercial sums.
- **RFC 7807 Problem Details for HTTP APIs**: Standardizing HTTP error responses (`type`, `title`, `status`, `detail`, `code`, `errors`).
- **PostgreSQL xmin vs. Concurrency Tokens**: How EF Core uses `.IsRowVersion()` on `uint` to map to PostgreSQL's `xmin` transaction column, and the necessity of handling default values in secondary test providers.

---

## 8. Intentionally Introduced Failures & Investigations

1. **Failure: Nullable Complex Property in EF Core InMemory**:
   - *Observation*: `KeyNotFoundException` during query execution against EF Core's InMemory provider.
   - *Root Cause*: InMemory database provider lacks relational null-column support for `ComplexProperty`.
   - *Resolution*: Replaced InMemory database with SQLite in-memory featuring real relational table schema via `EnsureCreated()`.

2. **Failure: State Mutation on Failed Stock Reservation**:
   - *Observation*: If inventory reservation threw an error or returned insufficient stock, `order.Submit()` had already updated `Status = OrderStatus.Submitted` in memory.
   - *Root Cause*: Premature state mutation before external confirmation.
   - *Resolution*: Added `order.ValidateCanSubmit()`, called `_inventoryService.ReserveStockAsync()`, and executed `order.Submit()` strictly after confirmation.

3. **Failure: EF Core Multiple Database Provider Collision**:
   - *Observation*: `InvalidOperationException: Services for database providers 'Npgsql', 'Sqlite' have been registered`.
   - *Root Cause*: `WebApplicationFactory` replaced DbContext options but left Npgsql options configurator in the DI service collection.
   - *Resolution*: Cleared all EF Core and DbContext descriptors and used an isolated internal service provider for SQLite in test factory.

4. **Failure: Concurrency Token NOT NULL Constraint in SQLite**:
   - *Observation*: `SQLite Error 19: 'NOT NULL constraint failed: orders.Version'`.
   - *Root Cause*: `Version` was mapped as `.IsRowVersion()` (PostgreSQL `xmin`). SQLite does not auto-generate `uint` row versions on insert.
   - *Resolution*: Added conditional default value configuration (`HasDefaultValue(0u)`) when running under the SQLite provider in `TradeFlowDbContext`.

---

## 9. Unanswered & Open Questions

- **[Q08](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/OPEN-QUESTIONS.md#L93)** (*In-Memory Databases vs. Testcontainers for Integration Tests*):
  - Updated status from `New` to `Good`. The developer witnessed concrete failures in both EF Core InMemory (complex property failure) and SQLite (rowversion column constraint failure), proving why Testcontainers with PostgreSQL is the production standard.

---

## 10. Professional English & Interview Practice Notes

- **Key Phrasing Mastered**:
  - *"Immutable commercial snapshot frozen at point-of-sale"*
  - *"Database lock contention on high-frequency inventory rows"*
  - *"Stateless aggregate rehydration across HTTP request boundaries"*
  - *"Optimistic Concurrency Control (OCC) using row version tokens"*
  - *"Unified error contract adherence conforming to RFC 7807 Problem Details"*

---

## 11. Next Learning Step

- **Topic**: Phase 1 Retrospective & Transition to Phase 2 (Modular Monolith Boundaries & Architecture Testing).
- **Objectives**:
  - Introduce `NetArchTest.Rules` to enforce architectural constraints automatically.
  - Explore module boundary definition between `Orders`, `Inventory`, and `Catalog`.
  - Prepare for asynchronous boundary transition via Transactional Outbox.
