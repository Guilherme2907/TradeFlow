# ADR-0018: Frontend SPA Architecture, OAuth2 PKCE In-Memory Auth, White-Label Keycloak Theme & Testing Pyramid

- **Status**: Accepted
- **Date**: 2026-11-16
- **Deciders**: Developer (Lead Architect), AI Mentor
- **Relevant Context**: Phase 10 (Frontend Architecture, Transparent SSO Identity & B2B Trading Terminal)

---

## 1. Context & Background

TradeFlow's backend Modular Monolith (.NET 10) exposes RESTful Minimal APIs secured by Keycloak OAuth2/OIDC JWTs (`ADR-0013`), Redis Cache-Aside (`ADR-0014`), OpenTelemetry tracing (`ADR-0015`), and Polly v8 resilience (`ADR-0016`). To complete the full-stack platform, we must introduce an interactive B2B Trading Terminal web frontend with seamless, visually transparent Single Sign-On (SSO) powered by Keycloak.

---

## 2. Problem Statement

We must determine:
1. **Rendering & Hosting Strategy**: Whether to build a Client-Side Rendered SPA (Vite + React) or an SSR application (Next.js).
2. **Browser Token Storage**: How to store OAuth2 Access Tokens in the browser without exposing long-lived credentials to trivial XSS extraction.
3. **White-Label SSO Experience**: How to make Keycloak's login flow visually identical ("transparent") to the TradeFlow frontend template without introducing heavy Java/Maven build pipelines.
4. **Frontend Testing Strategy**: How to balance fast, behavior-focused unit/component testing with high-confidence full-stack browser E2E testing.

---

## 3. Options Considered

### Rendering Architecture
- **Option A: Vite + React 19 + TypeScript (CSR / SPA)**
  - *Pros*: Clean decoupling from the .NET backend; compiles to lightweight static assets (CDN/Nginx) with no Node.js runtime server needed in Kubernetes; rich desktop-like interactivity for authenticated B2B dashboards where public SEO is irrelevant.
  - *Cons*: Initial JS bundle download before first render; unsuited for public SEO landing pages.
- **Option B: Next.js (SSR / React Server Components)**
  - *Pros*: Server-rendered HTML and SEO optimization.
  - *Cons*: Requires running and scaling a Node.js compute tier in Kubernetes alongside .NET solely for authenticated screens that receive zero search-engine traffic.

### Token Storage & Auth Flow
- **Option A: Authorization Code Flow + PKCE with In-Memory Token Storage (`oidc-client-ts`)**
  - *Pros*: Eliminates `localStorage`/`sessionStorage` token theft vectors; browser never touches user passwords (delegated to Keycloak); supports Silent Renew / top-level SSO bounce on page refresh via Keycloak's HttpOnly session cookie.
  - *Cons*: Page reloads re-acquire tokens silently via Keycloak; active in-tab XSS could still intercept live `fetch` calls while the tab is open (mitigated further via CSP).
- **Option B: OAuth2 Tokens in `localStorage`**
  - *Pros*: Survives F5 natively.
  - *Cons*: Any XSS script or compromised third-party package can trivially exfiltrate tokens via `localStorage.getItem(...)`.
- **Option C: Full BFF (Backend-for-Frontend) Reverse Proxy**
  - *Pros*: Tokens stay completely off the browser behind `HttpOnly; SameSite=Strict` cookies.
  - *Cons*: Adds stateful reverse-proxy session infrastructure prematurely for Phase 10 (remains the clear upgrade path for ultra-strict production banking compliance).

### Keycloak White-Label Theme Strategy
- **Option A: Native FreeMarker Theme (`.ftl`) + Shared CSS Design Tokens (`tokens.css`)**
  - *Pros*: Zero Java/Maven toolchain required; mounted directly via Docker volume for instant hot-reload; shares identical CSS variables, typography, colors, SVG brand mark, and card geometry with the React Design System.
  - *Cons*: Login UI markup is maintained separately in `.ftl` HTML from JSX components.
- **Option B: Keycloakify (React-to-JAR Theme Compiler)**
  - *Pros*: Allows writing Keycloak themes directly in React/JSX.
  - *Cons*: Requires JDK + Maven build pipelines to produce `.jar` artifacts on every theme tweak.

---

## 4. Decision

We adopt:
1. **SPA (Vite + React 19 + TypeScript + Tailwind CSS)** organized in feature-based slices (`features/auth`, `features/orders`, `features/inventory`, `shared/`).
2. **Server State via TanStack Query (`@tanstack/react-query`)** strictly separated from ephemeral UI state (`useState`/local state).
3. **OAuth2 Authorization Code Flow + PKCE (`S256`)** via `oidc-client-ts` storing tokens **strictly in RAM** with automatic Silent Renew, backed by explicit **CORS Origin Allowlisting** on `TradeFlow.Api` (`Cors:AllowedOrigins`) and a dedicated Keycloak public SPA client (`tradeflow-web`).
4. **Native Keycloak FreeMarker Theme (`tradeflow`)** sharing an identical **Dark-Fintech CSS Design Token contract (`tokens.css`)** with the React frontend for a visually seamless SSO transition.
5. **Balanced Frontend Testing Pyramid**:
   - **Vitest + React Testing Library**: Pure frontend logic (RFC 7807 `ProblemDetails` parsing, monetary calculations) and user-centric component behavior (form validation, RBAC action visibility, error states) without mocking internal state.
   - **Playwright E2E**: Reserved strictly for critical full-stack browser journeys (Keycloak white-label SSO redirect -> login -> return to dashboard -> order creation/submission -> F5 persistence).

---

## 5. Trade-offs & Consequences

- **Positive Consequences**:
  - True architectural parity between backend Modular Monolith and frontend feature slices.
  - Zero password handling in frontend JavaScript; zero token persistence in browser storage.
  - Visually cohesive SSO login experience with minimal DevOps friction.
- **Negative Consequences / Costs**:
  - Browser `OPTIONS` preflight CORS rules must be maintained accurately across local, Docker, and Kubernetes environments.
  - Two small template surfaces (`login.ftl` and React components) must respect the shared `tokens.css` contract.

---

## 6. Conditions for Revisiting

- Revisit **SPA -> BFF Proxy** if regulatory security audits prohibit bearer tokens in browser memory altogether.
- Revisit **FreeMarker -> Keycloakify** if custom multi-step onboarding, Passkeys/WebAuthn UIs, or tenant-specific registration wizards expand beyond login/error screens.
