# ADR-0013: Stateless Federated Identity (OAuth2 / OIDC), RBAC & BOLA/IDOR Tenant Isolation

- **Status**: Accepted
- **Date**: 2026-10-30
- **Deciders**: Architecture & Engineering Mentor / Developer
- **Phase**: Phase 5 — Security, Identity & Access Control

---

## 1. Context & Problem Statement

In a B2B trading and financial order execution platform (`TradeFlow`), unauthenticated or poorly scoped endpoints expose catastrophic business risks:
1. **Unauthenticated Order Manipulation**: Anonymous callers could create, modify, submit, or cancel orders.
2. **Broken Object Level Authorization (BOLA / IDOR — OWASP API1:2023)**: If the API accepts `customerId` in the JSON request body (`POST /api/orders`) or fails to verify tenant ownership when mutating an existing order (`POST /api/orders/{id}/lines`, `POST /api/orders/{id}/submit`, `POST /api/orders/{id}/cancel`), a malicious trader from Organization A could forge Organization B's `customerId` and execute or sabotage trades on their behalf.
3. **Privilege Escalation**: Different institutional personas (`Trader`, `RiskManager`, `ComplianceAuditor`, `Admin`) have strictly distinct operational capabilities. For instance, a `ComplianceAuditor` must never create orders, whereas a `RiskManager` must be capable of emergency order cancellation across tenants during market anomalies.
4. **Framework Coupling in Clean Architecture**: Injecting `HttpContext` or `ClaimsPrincipal` directly into `TradeFlow.Application` or `TradeFlow.Domain` would couple core business orchestration to ASP.NET Core web primitives, breaking pure unit testing and violating the Dependency Inversion Principle (DIP).

---

## 2. Decision

We adopt **Stateless Federated Identity via OpenID Connect (OIDC) / OAuth 2.0 (Keycloak)** at the HTTP edge combined with a clean **`ICurrentUserContext`** abstraction and **Tenant Ownership Enforcement** in the Application layer:

### 2.1 Cryptographic JWT Validation at the API Edge
- **Identity Provider (IdP)**: **Keycloak 26** (`quay.io/keycloak/keycloak:26.1.0`) manages users, credentials, institutional tenant attributes (`customer_id`), and realm roles (`Trader`, `RiskManager`, `ComplianceAuditor`, `Admin`).
- **Edge Validation**: `TradeFlow.Api` validates Bearer JWTs (`Microsoft.AspNetCore.Authentication.JwtBearer`) at the HTTP middleware pipeline using OIDC Discovery / JWKS public keys, verifying signature (`RS256`), issuer (`iss`), audience (`aud`), and token lifetime (`exp` / `nbf`).
- **Role Claim Normalization**: Keycloak's nested `realm_access.roles` JSON claim and flat `role`/`roles` claims are normalized into standard `ClaimTypes.Role` claims during token validation.

### 2.2 Clean Architecture Decoupling via `ICurrentUserContext`
- `TradeFlow.Application` defines the transport-agnostic [`ICurrentUserContext`](../../src/TradeFlow.Application/Common/Security/ICurrentUserContext.cs) interface (`IsAuthenticated`, `UserId`, `CustomerId`, `Roles`, `IsInRole(string)`).
- `TradeFlow.Api` provides the concrete [`CurrentUserContext`](../../src/TradeFlow.Api/Common/Security/CurrentUserContext.cs) adapter backed by `IHttpContextAccessor`.
- Architectural fitness tests (`NetArchTest.Rules`) strictly prohibit `TradeFlow.Application` and `TradeFlow.Domain` from referencing `Microsoft.AspNetCore.*` or `System.Security.Claims`.

### 2.3 BOLA / IDOR Prevention via Cryptographic `customer_id` Claim
- `CustomerId` is **removed from `CreateOrderRequest` and `CreateOrderCommand`**. Callers can never supply their own `CustomerId` in the HTTP payload.
- Instead, `CreateOrderCommandHandler` extracts `CustomerId` exclusively from `ICurrentUserContext.CustomerId` (populated from the cryptographically signed `customer_id` JWT claim). If the claim is missing, the command fails with `403 Forbidden` (`CUSTOMER_TENANT_REQUIRED`).
- When mutating an existing order (`AddOrderLineCommandHandler`, `SubmitOrderCommandHandler`, `CancelOrderCommandHandler`), the handler verifies that `order.CustomerId == currentUser.CustomerId` unless the caller holds privileged cross-tenant authority (`Admin`, or `RiskManager` for emergency order cancellation). Violations return `403 Forbidden` (`ORDER_TENANT_FORBIDDEN`).

### 2.4 Role-Based Access Control (RBAC) Policies
| Policy | Allowed Roles | Protected Operations |
| :--- | :--- | :--- |
| `OrderWritePolicy` | `Trader`, `Admin` | `POST /api/orders`, `POST /api/orders/{id}/lines`, `POST /api/orders/{id}/submit` |
| `OrderCancelPolicy` | `Trader`, `RiskManager`, `Admin` | `POST /api/orders/{id}/cancel` |
| `OrderReadPolicy` | `Trader`, `RiskManager`, `ComplianceAuditor`, `Admin` | `GET /api/orders/{id}` |

---

## 3. Consequences

### Positive
- **Zero Trust on Client Payloads**: Eliminates BOLA/IDOR vulnerabilities by binding order creation and mutation to the signed `customer_id` claim inside the JWT.
- **Pure Application Testability**: Application unit tests mock `ICurrentUserContext` in 2 lines without constructing `HttpContext` or `ClaimsPrincipal`.
- **Horizontal Scalability**: Stateless JWT verification via cached OIDC JWKS keys requires zero database or session lookups per request.

### Trade-offs & Operational Considerations
- **Token Revocation Window**: Because JWT access tokens are verified statelessly, a revoked user or changed `customer_id` remains valid until the access token's `exp` timestamp expires. Production deployments mitigate this by enforcing short access token lifetimes (e.g., 5 minutes) paired with OAuth2 Refresh Tokens.
