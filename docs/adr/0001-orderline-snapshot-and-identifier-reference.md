# ADR-0001: OrderLine Snapshot and Identifier Reference Across Bounded Contexts

- **Status**: Accepted
- **Date**: 2026-09-28
- **Deciders**: Developer & AI Mentor (Lead Architect)
- **Relevant Issues / Context**: Phase 1 Core Domain & Clean Monolith Kickoff, [Open Question Q03](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/OPEN-QUESTIONS.md#L41)

---

## 1. Context & Background
In TradeFlow, a B2B commerce and order-management platform, the `Orders` module manages customer purchase orders, while the `Catalog` module manages products, SKUs, and pricing. When a buyer submits an order, line items correspond to products found in the catalog.

In relational and ORM-centric architectures (like EF Core), developers frequently map line items with direct foreign key relationships and navigation properties to the product table (`OrderLine.Product`).

## 2. Problem Statement
How should the `OrderLine` entity in the `Orders` bounded context reference the product in the `Catalog` bounded context? Specifically:
1. How do we ensure historical financial integrity when a product's price, description, or status changes in the catalog after an order is placed?
2. How do we avoid tight entity and database coupling between `Orders` and `Catalog` so they can evolve independently and be split into separate schemas or services in future phases?

## 3. Options Considered

### Option A: Direct EF Core Foreign Key & Navigation Property (`OrderLine.Product`)
- `OrderLine` stores `ProductId` as a relational FK and references `Product` directly. Line totals are computed dynamically from `Product.Price`.
- *Pros*:
  - Simple initial ORM mapping.
  - Relational database-level foreign key constraint prevents referencing non-existent products.
- *Cons*:
  - **Fatal Financial Flaw**: When catalog prices update, past order totals retroactively change, breaking historical accounting, tax reporting, and invoices.
  - Deleting or deprecating a product breaks or cascades into historical orders.
  - Tightly couples the `Orders` schema to the `Catalog` schema, making future database or service separation difficult.

### Option B: Reference by Identifier with Immutable Point-in-Time Snapshot (Selected)
- `OrderLine` stores `ProductId` (strongly typed identifier / value object) without an ORM navigation property to `Product`.
- `OrderLine` captures an immutable snapshot of essential commercial attributes at the moment of order placement: `Sku`, `ProductName`, and `UnitPrice` (`Money` value object).
- *Pros*:
  - **Guaranteed Financial Integrity**: Historical orders remain legally and financially immutable regardless of future catalog modifications or product deprecations.
  - **Bounded Context Autonomy**: The `Orders` module does not depend on the `Catalog` entity model, fulfilling core DDD principles.
  - **Zero Database Coupling**: The tables can live in separate schemas or even separate databases with zero foreign key constraints across context boundaries.
- *Cons*:
  - Duplication of data (snapshot of name, SKU, price stored on each order line).
  - Validation that the product exists and is active must be performed at the application layer during order placement rather than by database foreign key constraints.

## 4. Decision
We select **Option B: Reference by Identifier with Immutable Point-in-Time Snapshot**.

An `Order` is a legally binding financial contract frozen at the moment of creation. `OrderLine` will hold `ProductId` plus an immutable snapshot of `Sku`, `ProductName`, and `UnitPrice` (`Money` value object). No navigation property or direct entity link to `Product` will exist in the domain model.

## 5. Trade-offs & Consequences

### Positive Consequences (Gains)
- Perfect auditability and historical financial immutability. Past invoices will always match recorded order lines.
- Complete bounded context decoupling: `Catalog` and `Orders` can be extracted into separate modules, database schemas, or microservices without modifying the `Order` aggregate.

### Negative Consequences (Costs / Liabilities)
- Minor data duplication across tables (acceptable trade-off in financial and commerce domains).
- Referential integrity must be validated in application use cases prior to order persistence.

## 6. Conditions for Revisiting
This decision represents a permanent financial domain rule. It would only be revisited if the business model shifts to dynamic variable contracts where line items are explicitly intended to float with live market rates (which would still be modeled via explicit repricing domain events rather than direct entity joins).
