# Study Session Checkpoint — Phase 4 (Apache Kafka Streaming) & Phase 5 (Identity, OAuth2/OIDC & Keycloak Security)

- **Date**: 2026-10-30
- **Stage**: Phase 4 Completed & Phase 5 Completed
- **Decisions Recorded**:
  - [ADR-0012: High-Throughput Event Streaming with Apache Kafka (KRaft), Partition Key Ordering, and Dead-Letter Topics](../adr/0012-high-throughput-event-streaming-apache-kafka.md)
  - [ADR-0013: Stateless Federated Identity (OAuth2 / OIDC), RBAC & BOLA/IDOR Tenant Isolation](../adr/0013-identity-oauth2-oidc-rbac-and-tenant-isolation.md)
- **Commits**:
  - `5920555`: `feat(streaming): implement high-throughput Kafka streaming, poison-pill DLT, bounded memory window and symmetrical outbox routing`
  - `d7b85a1`: `feat(security): implement stateless federated identity via Keycloak OIDC, RBAC policies, and BOLA/IDOR tenant isolation`

---

## 1. Executive Summary & Breakthroughs

During this session, TradeFlow advanced across two major foundational architectural milestones:

### Phase 4 (Apache Kafka Event Streaming):
1. **Decoupled Outbox Strategy**: Replaced hardcoded event dispatching with symmetrical routing directly declared in `KafkaTopology` and `RabbitMqTopology`, allowing any domain event from any module to stream without custom mapper classes.
2. **Head-of-Line Blocking Prevention**: Implemented poison-pill dead-letter topic routing (`tradeflow.market.trades.dlq`) with transient in-memory retry backoff before committing offsets.
3. **Memory Leak Protection**: Implemented a bounded sliding window (`DefaultMaxHistoryPerProduct = 100`) inside the singleton `MarketTradeAnalyticsHandler`.

### Phase 5 (Identity, Security & Tenant Isolation):
1. **Stateless Edge Authentication**: Integrated Keycloak 26.1 in Docker (`tradeflow` realm, RSA-2048 signing key, `tradeflow-api` client) validating JWTs statelessly via JWKS in ASP.NET Core without hitting databases on every request.
2. **Clean Architecture Decoupling**: Defined `ICurrentUserContext` in `TradeFlow.Application.Common.Security`, with concrete implementation `CurrentUserContext` in `TradeFlow.Api.Common.Security` backed by `IHttpContextAccessor`. Enforced via architectural tests (`LayerDependencyTests`).
3. **Defense Against BOLA / IDOR (OWASP API1:2023)**:
   - `CustomerId` was completely removed from the request payload of `CreateOrderCommand`.
   - Tenant identity is resolved exclusively from the cryptographically signed `customer_id` claim in the JWT.
   - Resource modification (`AddOrderLine`, `SubmitOrder`, `CancelOrder`) verifies tenant ownership, returning `403 Forbidden` (`ORDER_TENANT_FORBIDDEN`) on cross-tenant tampering.
   - Special cross-tenant emergency authority granted to `RiskManager` and `Admin`.
4. **Developer Experience & Scalar UI**: Created `POST /api/auth/token` endpoint and integrated Bearer security schemes into Scalar UI (`/scalar/v1`) for testing.

---

## 2. Test Suite Status

Total Passing Tests: **111 tests (100% green)**
- **Domain Unit Tests**: 41 tests (Aggregates, Value Objects, temporal audit tracking)
- **Application Unit Tests**: 20 tests (Command handlers, validation behaviors, tenant isolation logic)
- **Architecture Tests**: 18 tests (NetArchTest fitness rules, layer boundaries, Anti-Backdoor rule, no ASP.NET dependencies in Application)
- **API Integration Tests**: 9 tests (`Testcontainers.PostgreSql`, `TestJwtTokenGenerator`, 401/403/BOLA/lifecycle validations)
- **Infrastructure Integration Tests**: 23 tests (`Testcontainers.PostgreSql`, `Testcontainers.RabbitMq`, `Testcontainers.Kafka`)

---

## 3. Next Steps & Road Ahead

- **Phase 6 / Phase 7 Transition**:
  - Explore **Distributed Caching with Redis** (Cache-Aside pattern, Cache Stampede / Dog-piling mitigation via Mutex/Semaphore, Redis Pub/Sub, and Cache Invalidation strategies).
  - Production Observability & Distributed Tracing via **OpenTelemetry** (correlation between HTTP -> PostgreSQL -> RabbitMQ -> Kafka).
