# Session Checkpoint: Phase 10 — Frontend SPA, Transparent Keycloak White-Label SSO, Backend Read APIs & CORS

- **Date**: 2026-11-16
- **Project Stage**: Phase 10 (Adaptive Phase 6) — Frontend Architecture, Identidade SSO Transparente & B2B Trading Terminal
- **Focus Area**: React 19 + TypeScript + Vite SPA, OAuth2 PKCE In-Memory Auth, Shared Design System (`tokens.css`) + Keycloak FreeMarker Theme, Backend List Queries & CORS Allowlist (`ADR-0018`)

---

## 1. Implementation Progress

1. **Architecture Decision Record**:
   - Created and accepted [`ADR-0018`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0018-frontend-architecture-spa-pkce-white-label-keycloak-testing.md) and indexed in [`docs/adr/README.md`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/README.md).

2. **Backend Read APIs & Browser Security (CORS)**:
   - Added `ListAsync(CustomerId? customerId)` in [`IOrderRepository.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Orders/IOrderRepository.cs) & [`OrderRepository.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Modules/Orders/Persistence/OrderRepository.cs).
   - Added `GetAllAsync` in [`IInventoryRepository.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Domain/Inventory/IInventoryRepository.cs) & [`InventoryRepository.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Infrastructure/Modules/Inventory/Persistence/InventoryRepository.cs).
   - Extracted shared [`OrderMappings.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/Orders/Queries/OrderMappings.cs) and added [`GetOrdersQuery.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/Orders/Queries/GetOrders/GetOrdersQuery.cs), [`GetOrdersQueryHandler.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/Orders/Queries/GetOrders/GetOrdersQueryHandler.cs), [`GetInventoryItemsQuery.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/Inventory/Queries/GetInventoryItems/GetInventoryItemsQuery.cs), and [`GetInventoryItemsQueryHandler.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Application/Inventory/Queries/GetInventoryItems/GetInventoryItemsQueryHandler.cs).
   - Mapped `GET /api/orders` in [`OrderEndpoints.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Api/Endpoints/OrderEndpoints.cs) and `GET /api/inventory` in [`InventoryEndpoints.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Api/Endpoints/InventoryEndpoints.cs).
   - Configured strict origin allowlist `TradeFlowCorsPolicy` before `UseAuthentication()` / `UseTradeFlowResilience()` in [`Program.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Api/Program.cs), [`appsettings.json`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Api/appsettings.json), and [`appsettings.Development.json`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Api/appsettings.Development.json).

3. **Keycloak White-Label Theme & Public SPA Client (`tradeflow-web`)**:
   - Created FreeMarker theme [`docker/keycloak/themes/tradeflow/login/`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docker/keycloak/themes/tradeflow/login/theme.properties) (`login.ftl`, `error.ftl`, `tradeflow-login.css`, and shared [`tokens.css`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docker/keycloak/themes/tradeflow/login/resources/css/tokens.css)).
   - Added SPA client `tradeflow-web` (`standardFlowEnabled: true`, `directAccessGrantsEnabled: false`, PKCE `S256`, `customer-id-mapper`, `audience-mapper`, `realm-roles-mapper`) and `"loginTheme": "tradeflow"` in [`tradeflow-realm.json`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docker/keycloak/tradeflow-realm.json).
   - Mounted theme folder into Keycloak in [`docker-compose.yml`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docker-compose.yml).

4. **Frontend React 19 SPA (`src/TradeFlow.Web`)**:
   - Built modular feature-sliced SPA with shared [`tokens.css`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Web/src/styles/tokens.css), [`oidcConfig.ts`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Web/src/features/auth/oidcConfig.ts) (`InMemoryWebStorage`), [`AuthContext.tsx`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Web/src/features/auth/AuthContext.tsx) (StrictMode-safe PKCE callback + silent session restore), [`httpClient.ts`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Web/src/shared/api/httpClient.ts), [`problemDetails.ts`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Web/src/shared/api/problemDetails.ts), [`OrdersDashboardPage.tsx`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Web/src/features/orders/OrdersDashboardPage.tsx), [`OrderComposerPanel.tsx`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Web/src/features/orders/OrderComposerPanel.tsx), and [`OrderDetailPanel.tsx`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Web/src/features/orders/OrderDetailPanel.tsx).

---

## 2. Architectural Decisions Made

- Accepted [`ADR-0018`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/docs/adr/0018-frontend-architecture-spa-pkce-white-label-keycloak-testing.md):
  - **CSR/SPA (React + Vite)** over SSR (Next.js) for authenticated internal B2B dashboard needing zero Node.js server footprint in Kubernetes.
  - **OAuth2 PKCE + In-Memory `userStore` (`InMemoryWebStorage`)** over `localStorage` to block persistent XSS token harvesting, while allowing ephemeral `stateStore` PKCE verifier handshake over `sessionStorage` during top-level redirect.
  - **Native Keycloak FreeMarker Theme + Shared CSS Design Tokens (`tokens.css`)** over Keycloakify to avoid Java/Maven build overhead while guaranteeing visual continuity.

---

## 3. Testing Decisions Made

- **Backend**:
  - Unit tests: [`GetOrdersQueryHandlerTests.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.Application.UnitTests/Orders/GetOrdersQueryHandlerTests.cs) (3 tests for tenant filtering, BOLA blocking, and RiskManager visibility) & [`GetInventoryItemsQueryHandlerTests.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.Application.UnitTests/Inventory/GetInventoryItemsQueryHandlerTests.cs).
  - API non-Docker CORS test: [`CorsPolicyTests.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.Api.IntegrationTests/Endpoints/CorsPolicyTests.cs) (validates `OPTIONS` preflight for `http://localhost:5173` vs untrusted origins).
  - API Docker Integration tests: [`OrderEndpointTests.cs`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/tests/TradeFlow.Api.IntegrationTests/Endpoints/OrderEndpointTests.cs) verifying `GET /api/orders` tenant isolation and `GET /api/inventory`.
- **Frontend**:
  - Fast Unit/Component tests (Vitest + RTL, 5 passing): [`problemDetails.test.ts`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Web/src/shared/api/problemDetails.test.ts) & [`OrderDetailPanel.test.tsx`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Web/src/features/orders/OrderDetailPanel.test.tsx).
  - Critical E2E Spec (Scaffolded, deferred live run): [`playwright.config.ts`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Web/playwright.config.ts) & [`sso-order-flow.spec.ts`](file:///g:/Estudo/FullStack-Engineer-Plan/TradeFlow/src/TradeFlow.Web/e2e/sso-order-flow.spec.ts).

---

## 4. Important Bugs Caught & Operational Investigations

1. **Keycloak Container Not Recreated (`Client not found` & default gray theme)**:
   - *Cause*: Existing `tradeflow-keycloak` container kept old H2 DB (`--import-realm` uses `IGNORE_EXISTING`) and lacked the mounted `./docker/keycloak/themes/tradeflow` volume.
   - *Fix*: Recreated container with `docker compose up -d --force-recreate keycloak`, verified realm import and HTTP 200 custom theme delivery.
2. **Vitest Caught Unhandled Promise Rejection (`mutateAsync` vs `mutate`)**:
   - *Cause*: Calling `submitMutation.mutateAsync(...)` without `try/catch` threw an unhandled rejection when `OrderDetailPanel.test.tsx` simulated HTTP `409 Conflict`.
   - *Fix*: Switched UI handlers to idiomatic TanStack Query `.mutate(...)`, letting `mutation.error` feed `<ErrorBanner />` cleanly.
3. **Windows `npm.ps1` Execution Policy & Quoted `PATH`**:
   - *Cause*: PowerShell execution policy blocked `npm.ps1` and trailing quote on `C:\Program Files\nodejs\"` broke `esbuild`'s subshell lookup.
   - *Workaround*: Verified build/tests cleanly via `npm.cmd` with sanitized `$env:PATH`.

---

## 5. What Was Understood Well vs. Needs Review

- **Understood Well**: CSR/SPA static deployment advantages, `localStorage` XSS risk vs in-memory token storage + Silent Renew, `GET /api/orders` multi-tenant isolation, and CORS origin allowlisting.
- **Explicitly Marked `Needs Review`**: React fundamentals (`useState`, `useEffect`, `useContext`, TanStack Query mechanics), FreeMarker Keycloak templating, and Playwright E2E — developer explicitly decided to study React fundamentals independently before revisiting frontend depth.

---

## 6. Next Learning Step

- Proceed to the final curriculum pillars (chosen by developer):
  - **Performance & Load Testing (`k6` / latency p95-p99 / Redis stampede & pool saturation)**, OR
  - **Infrastructure as Code (`Terraform`)**, OR
  - **Chaos Engineering & Staff-Level Capstone Architecture Defense**.
