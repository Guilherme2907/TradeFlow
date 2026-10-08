# ADR-0004: Persistence Mapping via EF Core Complex Types and Backing Fields

- **Status**: Accepted
- **Date**: 2026-10-01
- **Deciders**: Developer & AI Mentor (Lead Architect)
- **Relevant Issues / Context**: Phase 1 Infrastructure & Persistence Layer Implementation

---

## 1. Context & Background
In Domain-Driven Design (DDD), domain entities and aggregate roots prioritize invariant protection, encapsulation, and expressive domain modeling over database schema convenience. 

Specifically:
- `Money` is a Value Object containing two scalar components (`Amount` and `Currency`).
- `Order` encapsulates its line items inside a private `List<OrderLine> _lines` backing field, exposing only a read-only `IReadOnlyCollection<OrderLine> Lines`.
- `OrderLine` has no independent lifecycle outside `Order`.
- Entities use strongly typed identifiers (`OrderId`, `OrderLineId`, etc.) and private parameterless constructors to block unvalidated instantiation.

## 2. Problem Statement
How should Entity Framework Core 10 map these encapsulated DDD constructs to a relational PostgreSQL database without violating encapsulation or compromising SQL query performance and data integrity?

## 3. Options Considered

### Option A: Public Setters and Primitive Types (Anemic CRUD Mapping)
- Relax domain encapsulation: add public setters to `Order.Lines`, replace `Money` with raw `decimal` columns (dropping currency or storing it in a separate property), and use raw `Guid` IDs everywhere.
- *Pros*: Trivial EF Core configuration.
- *Cons*: **Destroys Domain Invariants**. Any layer can manipulate order lines or create invalid money values, rendering domain invariant methods bypassable.

### Option B: PostgreSQL Native JSONB Document Columns
- Map complex value objects like `Money` and child collections like `_lines` directly into PostgreSQL `JSONB` columns.
- *Pros*: Keeps relational table schemas flat; matches in-memory object shapes.
- *Cons*: Severe penalties for financial aggregations (`SUM`, `AVG`), complicates indexing, eliminates native database `CHECK` constraints, and makes reporting/BI queries awkward.

### Option C: Complex Properties, Backing Fields, and Shadow Keys (Selected)
- Use EF Core `ComplexProperty` to map `Money` into flattened columns (`unit_price_amount NUMERIC(18,2)`, `unit_price_currency VARCHAR(3)`).
- Map `Order.Lines` to the private backing field `_lines` with `PropertyAccessMode.Field`, keeping the public property read-only.
- Configure `OrderId` as a shadow foreign key on `order_lines`.
- Use Value Converters for strongly typed IDs (`OrderId` $\leftrightarrow$ `Guid`).
- Map `Version` for Optimistic Concurrency Control.
- *Pros*:
  - **100% Domain Encapsulation**: Domain models remain pure with zero public setters and no anemic compromises.
  - **Native SQL Performance**: Full B-Tree indexing, native SQL aggregation (`SUM(unit_price_amount * quantity)`), and PostgreSQL check constraints.
  - Transparent persistence mapping.
- *Cons*:
  - Requires explicit Fluent API configuration in `IEntityTypeConfiguration<T>`.

## 4. Decision
We select **Option C: Complex Properties, Backing Fields, and Shadow Keys**.

1. `Money` will be mapped using EF Core `ComplexProperty` into dedicated `_amount` and `_currency` columns.
2. `Order.Lines` will be mapped to the private `_lines` backing field using `PropertyAccessMode.Field`.
3. Strongly typed identifiers will use custom EF Core Value Converters.
4. Parameterless constructors will remain `private`.

## 5. Trade-offs & Consequences

### Positive Consequences (Gains)
- Perfect preservation of domain invariants in production.
- High-performance relational queries, indexing, and aggregations in PostgreSQL.
- Database schema cleanly reflects relational normal forms while C# code adheres to rich DDD.

### Negative Consequences (Costs / Liabilities)
- Explicit Fluent API mapping code required for each aggregate and entity.

## 6. Conditions for Revisiting
This decision is foundational for relational persistence in Clean Architecture and will remain active for all relational relational aggregates.
