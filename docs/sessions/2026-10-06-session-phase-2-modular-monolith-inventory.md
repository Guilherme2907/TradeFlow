# Session Checkpoint: Phase 2 Modular Monolith & Inventory Context Integration

- **Date**: 2026-10-06
- **Project Stage**: Phase 2 — Modular Monolith & Strict Boundary Enforcement
- **Focus Area**: Architectural Fitness Functions (NetArchTest), Inventory Bounded Context Design, PostgreSQL Schema Isolation, and Explicit Module Contracts

---

## 1. Implementation Progress

1. **Automated Architectural Fitness Functions (`TradeFlow.ArchitectureTests`)**:
   - Integrated `NetArchTest.Rules` into a dedicated test assembly (`tests/TradeFlow.ArchitectureTests`).
   - Implemented 14 automated architectural fitness functions protecting:
     - Inward Clean Architecture dependency flow ([`LayerDependencyTests.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.ArchitectureTests/Layers/LayerDependencyTests.cs)).
     - Domain encapsulation (non-public setters, sealed domain events, parameterless constructors for EF Core rehydration) ([`DomainEncapsulationTests.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.ArchitectureTests/Domain/DomainEncapsulationTests.cs)).
     - Application design (sealed commands and handlers, Anti-Backdoor rule prohibiting MediatR injection inside handlers) ([`ApplicationDesignTests.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.ArchitectureTests/Application/ApplicationDesignTests.cs)).
     - Modular Monolith boundary isolation between `Orders` and `Inventory` ([`ModuleBoundaryTests.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.ArchitectureTests/Modules/ModuleBoundaryTests.cs)).
2. **Inventory Bounded Context Domain Model (`TradeFlow.Domain.Inventory`)**:
   - Strongly-typed identifier: [`InventoryItemId`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Inventory/InventoryItemId.cs).
   - Domain Aggregate Root: [`InventoryItem`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Inventory/InventoryItem.cs) encapsulating invariants: `QuantityOnHand`, `AllocatedQuantity`, and computed `AvailableQuantity`.
   - Domain Invariant Exception: [`InventoryInvariantException`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Inventory/Exceptions/InventoryInvariantException.cs) (`INSUFFICIENT_STOCK`, `INVALID_INVENTORY_QUANTITY`, `EXCESSIVE_STOCK_RELEASE`).
   - Domain Events: [`StockAllocatedDomainEvent`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Inventory/Events/InventoryEvents.cs), `StockReleasedDomainEvent`, `StockReplenishedDomainEvent`.
   - Domain Repository Interface: [`IInventoryRepository`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Inventory/IInventoryRepository.cs).
3. **Modular Monolith Relational Persistence & Schema Isolation (`TradeFlow.Infrastructure`)**:
   - Configured dedicated PostgreSQL schemas per bounded context (ADR-0007):
     - `orders.orders`, `orders.order_lines` via [`OrderConfiguration.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Persistence/Configurations/OrderConfiguration.cs) and [`OrderLineConfiguration.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Persistence/Configurations/OrderLineConfiguration.cs).
     - `inventory.inventory_items` via [`InventoryItemConfiguration.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Persistence/Configurations/InventoryItemConfiguration.cs).
   - Added Optimistic Concurrency Control (OCC) rowversion concurrency tokens to prevent race conditions during concurrent stock reservations.
   - Implemented [`InventoryRepository`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Persistence/Repositories/InventoryRepository.cs).
4. **Explicit Module Contract Facade & Adapter (`TradeFlow.Application`)**:
   - Defined public contract DTOs: [`ReserveStockRequest`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/Inventory/Contracts/ReserveStockRequest.cs), `ReservationItemDto`.
   - Defined public module facade interface: [`IInventoryModule`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/Inventory/Contracts/IInventoryModule.cs).
   - Implemented in-process module coordinator: [`InventoryModule`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/Inventory/Services/InventoryModule.cs) executing two-phase validation and allocation across multiple aggregates.
   - Implemented [`InventoryReservationServiceAdapter`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Services/InventoryReservationServiceAdapter.cs) bridging Orders gateway to the Inventory public facade.
5. **Cross-Context Identifier Decoupling (`TradeFlow.Domain.Common`)**:
   - Extracted [`ProductId`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Common/ProductId.cs) into `TradeFlow.Domain.Common` to completely eliminate any dependency from `Inventory` to `Orders`.

---

## 2. Architectural Decisions Made

- **[ADR-0006](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0006-architectural-fitness-functions-and-rules.md): Automated Architectural Fitness Functions via NetArchTest**:
  - Adopted automated CI/CD architecture testing to guarantee inward layer dependency flow, aggregate encapsulation (non-public setters, parameterless constructors), and the Anti-Backdoor rule.
- **[ADR-0007](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0007-modular-monolith-schema-isolation-and-module-contracts.md): Modular Monolith PostgreSQL Schema Isolation and Explicit Module Contracts**:
  - Decided against flat tables and against opaque in-process MediatR command bus across modules.
  - Selected PostgreSQL schema isolation (`orders.*` and `inventory.*`) and explicit strongly-typed module interfaces (`IInventoryModule`) taking dedicated immutable contract DTOs (`ReserveStockRequest`).

---

## 3. Testing Decisions Made

- **Architecture Tests (`NetArchTest.Rules`)**:
  - Selected reflection-based static analysis running as xUnit tests. Executed in under 150ms in CI without database or container requirements.
- **Pure Unit Tests**:
  - 11 new pure unit tests for [`InventoryItemTests.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.Domain.UnitTests/Inventory/InventoryItemTests.cs) (total 37 domain tests). Zero mocks.
  - 3 new application unit tests for [`InventoryModuleTests.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.Application.UnitTests/Inventory/InventoryModuleTests.cs) verifying two-phase atomic stock reservation.
- **API Contract Tests with SQLite Seeding**:
  - Extended [`OrderEndpointTests.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.Api.IntegrationTests/Endpoints/OrderEndpointTests.cs) to seed real inventory items before submitting orders, verifying end-to-end HTTP lifecycle through the real in-process module facade.
- **What Was Decided NOT to Test**:
  - Did NOT test trivial DTO mappings or EF Core builder syntax in isolation; protected them through domain unit and API integration tests.
  - Tagged Docker-dependent Testcontainers integration tests with `[Trait("Category", "RequiresDocker")]` so fast tests run deterministically even when Docker Desktop daemon is stopped locally.

---

## 4. Important Tests Added & What They Protect

1. **`OrdersDomain_ShouldNotDependOn_InventoryDomain` & `InventoryDomain_ShouldNotDependOn_OrdersDomain`**:
   - **Protected Behavior**: Guarantees bounded context autonomy. Neither domain aggregate touches the other.
   - **Bug Prevention**: Prevents developers from casually adding navigation properties or foreign entity dependencies between modules.
2. **`CommandHandlers_ShouldNotDependOn_ISenderOrIMediator_AntiBackdoorRule`**:
   - **Protected Behavior**: Enforces ADR-0003 single-responsibility and prohibits recursive mediator orchestration.
   - **Bug Prevention**: Prevents hidden dependency chains and opaque distributed-spaghetti inside handlers.
3. **`ReserveStockAsync_WhenOneItemHasInsufficientStock_ShouldFailWithoutAllocatingAny`**:
   - **Protected Behavior**: Protects multi-item atomic stock reservation. If any SKU in a 10-item order has insufficient stock, zero items are allocated.
   - **Bug Prevention**: Prevents partial order allocations and stranded inventory reservations.
4. **`FullOrderLifecycle_ShouldCompleteSuccessfully` (API Integration)**:
   - **Protected Behavior**: Protects full order intake from `POST /api/orders` to `POST /api/orders/{id}/lines` to `POST /api/orders/{id}/submit`, validating cross-module inventory reservation in the database.

---

## 5. What Was Understood Well

- Clean Architecture dependency inversion rules and how to express them in NetArchTest.
- PostgreSQL schema isolation benefits over flat schemas (hard boundary defense against cross-context LINQ joins at zero operational cost).
- The superiority of explicit interfaces (`IInventoryModule`) over opaque in-process MediatR commands for module boundaries (compile-time discoverability and clear contract definitions).

---

## 6. What Was Struggled With / Conceptual Gaps

- **Architectural Placement of Module Facades**:
  - Initially placed `InventoryModule` in `Infrastructure.Services`, causing compilation failure in `Application.UnitTests` because Application cannot reference Infrastructure.
  - **Resolution**: Recognized that `InventoryModule` is an application service/facade coordinating use-case logic and domain repositories, so it naturally belongs in `TradeFlow.Application.Inventory.Services`.
- **Accidental Coupling via Identifiers**:
  - `ProductId` originally lived in `TradeFlow.Domain.Orders.Identifiers`. When `InventoryItem` needed it, `Inventory` accidentally depended on `Orders`.
  - **Resolution**: Extracted `ProductId` to `TradeFlow.Domain.Common`, restoring complete autonomy between both contexts.

---

## 7. Important Technical Concepts Explored

- **Architectural Fitness Functions**: Codifying architecture constraints into unit tests to prevent structural degradation over time.
- **Bounded Context Autonomy**: How two aggregates in different contexts refer to the same physical entity (e.g. Product) via identifier snapshots rather than shared references.
- **Two-Phase Allocation in Module Facades**: Validating availability across all items before executing state mutations on individual aggregates to guarantee atomicity.
- **Docker Trait Segregation**: Using xUnit traits (`[Trait("Category", "RequiresDocker")]`) to separate unit/architecture/SQLite tests from infrastructure tests requiring active container engines.

---

## 8. Intentionally Introduced Failures & Investigations

- **Failure 1: CS0234 Inward Layer Violation During Test Execution**:
  - *Observed*: `Application.UnitTests` failed to compile referencing `InventoryModule` in `Infrastructure`.
  - *Root Cause*: Clean Architecture strictly prohibits Application from depending on Infrastructure.
  - *Resolution*: Moved `InventoryModule` to `TradeFlow.Application.Inventory.Services`.
- **Failure 2: HTTP 404 Returned on Order Submission**:
  - *Observed*: `OrderEndpointTests.FullOrderLifecycle_ShouldCompleteSuccessfully` received HTTP 404 instead of 200.
  - *Root Cause*: Replacing the Phase 1 fake with real inventory checking caused `InventoryModule` to search the database for `ProductId`. Since none was seeded in the test, it returned `INVENTORY_ITEM_NOT_FOUND`, mapped to 404.
  - *Resolution*: Seeded an `InventoryItem` with 100 available stock in the test prior to submission.

---

## 9. Unanswered & Open Questions

- Updated **Q03: Boundary Enforcement in a Modular Monolith** from `New` to `Strong`.
- Updated **Q12: Automated Architectural Boundary Enforcement** from `New` to `Strong`.

---

## 10. Professional English & Interview Practice Notes

- Practiced framing the distinction between microservices and modular monoliths:
  *"Instead of prematurely introducing the operational complexity of distributed networks, we enforce hard bounded context boundaries in a modular monolith using PostgreSQL schema isolation (`orders.*` vs `inventory.*`) and explicit in-process contract facades (`IInventoryModule`), guarded by automated NetArchTest fitness functions."*

---

## 11. Next Learning Step

- **Phase 2 Deepening**:
  - High-concurrency race conditions on hot inventory items: Testing Optimistic Concurrency Control (OCC) vs. pessimistic locking (`SELECT FOR UPDATE`).
  - Stock replenishment commands and inventory query projections.
  - Transition preparation toward Phase 3: Identifying the friction points of synchronous monolith transaction boundaries that require the Transactional Outbox Pattern and RabbitMQ.
