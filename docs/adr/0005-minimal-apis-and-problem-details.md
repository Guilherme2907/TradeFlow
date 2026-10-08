# ADR-0005: Presentation Layer via Minimal APIs and RFC 7807 Problem Details

- **Status**: Accepted
- **Date**: 2026-10-04
- **Deciders**: Developer & AI Mentor (Lead Architect)
- **Relevant Issues / Context**: Phase 1 Presentation Layer Design & HTTP Contract Standards

---

## 1. Context & Background
The TradeFlow Web API serves as the primary HTTP entry point for B2B client applications, operational terminals, and downstream integrations. 

In Phase 1, we must establish the presentation layer architecture for exposing application use cases via HTTP, defining how requests are routed and how errors are communicated across system boundaries.

## 2. Problem Statement
1. How should HTTP endpoints be organized and dispatched: traditional ASP.NET Core MVC Controllers or Minimal APIs?
2. How should API errors (validation failures, domain rule breaches, resource not found, concurrency conflicts) be serialized: ad-hoc custom JSON formats or the standardized RFC 7807 Problem Details specification?

## 3. Options Considered

### Option A: Traditional ASP.NET Core MVC Controllers with Ad-Hoc JSON Errors
- Organize endpoints in `ControllerBase` classes.
- Return custom anonymous objects on failure (e.g. `BadRequest(new { error = ex.Message })`).
- *Pros*: Familiar syntax for legacy .NET developers.
- *Cons*: Heavy reflection startup overhead, unnecessary MVC pipeline bloat, and unpredictable error formats that force API clients to parse error strings.

### Option B: Modular Minimal APIs with RFC 7807 Problem Details (Selected)
- Implement endpoints using ASP.NET Core Minimal APIs organized into feature-centric endpoint group classes via extension methods (`app.MapOrderEndpoints()`).
- Map application `Result` errors directly to standardized **RFC 7807 Problem Details** (`application/problem+json`).
- *Pros*:
  - **High Throughput & Low Allocation**: Skips the MVC reflection pipeline and action filter overhead.
  - **Clean Code Organization**: Feature-based endpoint route groups prevent monolithic `Program.cs` bloat.
  - **Standardized Client Error Contracts**: Provides predictable machine-readable error responses (`type`, `title`, `status`, `detail`, `instance`, `code`, `errors`), enabling seamless error handling in frontend clients (React/TanStack Query) and API gateways.
  - **Identical Testability**: Full HTTP contract verification using `WebApplicationFactory<Program>`.
- *Cons*:
  - Requires explicit endpoint mapping extension methods rather than automatic assembly scanning.

## 4. Decision
We select **Option B: Modular Minimal APIs with RFC 7807 Problem Details**.

1. Endpoints will be implemented as Minimal APIs grouped by feature slices using `MapGroup("/api/...")`.
2. Application `Result` errors will be converted into RFC 7807 `ProblemDetails` via a centralized mapping utility:
   - `ErrorType.Validation` $\rightarrow$ `400 Bad Request`
   - `ErrorType.NotFound` $\rightarrow$ `404 Not Found`
   - `ErrorType.Conflict` $\rightarrow$ `409 Conflict`
   - `ErrorType.Failure` $\rightarrow$ `500 Internal Server Error`
3. The content type for all error responses will be `application/problem+json`.

## 5. Trade-offs & Consequences

### Positive Consequences (Gains)
- Optimized startup and request execution time.
- Machine-readable, uniform error contracts for all API consumers.
- Perfect foundation for automated OpenAPI documentation and frontend type generation.

### Negative Consequences (Costs / Liabilities)
- Explicit routing registration required in `Program.cs` for each endpoint group.

## 6. Conditions for Revisiting
This decision will remain the presentation standard across all monolith and microservice HTTP endpoints.
