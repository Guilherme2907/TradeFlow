# ADR-0020: Provider-Agnostic LLM Integration via `Microsoft.Extensions.AI`, Structured Outputs, and LLM Resilience

- **Status**: Accepted
- **Date**: 2026-11-24
- **Deciders**: Architecture & Engineering Mentorship Team
- **Phase**: Phase 12 — Applied AI Engineering (`Stage 12.1`)

---

## 1. Context and Problem Statement

TradeFlow B2B traders frequently receive unstructured purchase requests (e-mails, chat messages, spreadsheets pasted as free text) referencing industrial commodities, quantities, currencies, and minimum order constraints. Translating unstructured text into verified draft orders manually is error-prone and slow.

Introducing Large Language Models (LLMs) into a financial B2B trading platform introduces five systemic production risks:

1. **Vendor Lock-In**: Directly coupling `TradeFlow.Application` to a proprietary SDK (e.g., Google Gemini SDK or OpenAI SDK) prevents switching between cloud models (`gemini-2.5-flash`) and local/self-hosted inference (`Ollama` / `vLLM`) for air-gapped or cost-sensitive deployments.
2. **Financial Hallucination & Prompt Injection**: Asking an LLM to output prices, product GUIDs, or `CustomerId` tenants allows prompt injection or model hallucination to fabricate commercial terms (e.g., pricing Copper Grade A at `$0.01`) or breach tenant boundaries (`ADR-0013`).
3. **Brittle Free-Text Parsing**: Prompting an LLM to *"return valid JSON without markdown"* frequently breaks `JsonSerializer.Deserialize` in production when models emit conversational preambles or malformed fields.
4. **Token-Billing Amplification via Naive Retries/Timeouts**: Applying standard REST timeouts (e.g., `2–3s`) to LLM token generation aborts in-flight inference right before completion while still consuming input/output token quota, and blind retries multiply token costs by `3x–4x`.
5. **CI/CD Flakiness and Cost**: Calling live cloud LLMs on every `dotnet test` run introduces non-determinism, rate-limit failures (`HTTP 429`), multi-second latency, and recurring API charges.

---

## 2. Decision Outcome

We adopt the following architecture for **Stage 12.1 (`Order Intake Copilot`)** and subsequent `TradeFlow.AI` capabilities:

### 2.1 Provider-Agnostic Abstraction (`Microsoft.Extensions.AI`)
- `TradeFlow.Application` depends exclusively on `Microsoft.Extensions.AI` (`IChatClient`, `ChatMessage`, `ChatOptions`, `ChatResponseFormat`).
- `TradeFlow.Infrastructure.Modules.AI` implements concrete `IChatClient` adapters (`GeminiChatClient` for Google Gemini REST `v1beta` and `OllamaChatClient` for local Ollama `/api/chat`), selected at runtime via `AI:Provider` configuration (`Gemini` or `Ollama`) and wrapped in `ChatClientBuilder` with `.UseLogging()` and `.UseOpenTelemetry()`.

### 2.2 Strict AI vs. Domain Security Boundary ("Extract Intent, Never Authority")
- The LLM extraction schema (`LlmOrderExtractionSchema`) intentionally **omits** `UnitPrice`, `ProductId`, and `CustomerId`. It is structurally impossible for the model to dictate commercial prices or tenant ownership.
- `ParseOrderDraftCommandHandler` loads active commodities via `ICatalogModule` (`ADR-0007`), resolves authoritative prices, currencies, and `MinimumOrderQuantity` in deterministic C# code, quarantines unknown/hallucinated SKUs into `UnmatchedRequests` / `Warnings`, and scopes execution strictly to `ICurrentUserContext` (`ADR-0013`).
- `POST /api/ai/orders/parse-draft` **never mutates the database**; it returns a verified `OrderDraftProposalResponse` for explicit trader review (**Human-in-the-Loop**) before `POST /api/orders` is invoked.

### 2.3 Native Constrained Decoding (`Structured Outputs`) & SSE Streaming
- All structured extraction calls pass a strict JSON Schema via `ChatResponseFormat.ForJsonSchema(...)` (`responseJsonSchema` in Gemini `v1beta`; `format` schema object in Ollama), enforcing schema compliance at token-generation time rather than via prompt pleading.
- Interactive market/order advisory text uses `IChatClient.GetStreamingResponseAsync(...)` streamed over HTTP `text/event-stream` (SSE) to minimize perceived Time-To-First-Token (TTFT).

### 2.4 LLM-Aware Polly v8 Resilience & Graceful Degradation
- **Realistic Attempt Timeout**: `25s` per attempt (`65s` total pipeline timeout) to accommodate multi-hundred-token generation without premature cancellation.
- **Selective Retry**: Up to `2` retries with exponential backoff + jitter **strictly** on HTTP `429 Too Many Requests`, `502`/`503`/`504`, or network transport errors. **`TimeoutRejectedException` is intentionally excluded from retry** to prevent token-billing amplification storms.
- **Circuit Breaker & Fail-Safe UX**: Opens at `50%` failure ratio (minimum throughput `5` over `30s`, break duration `30s`), returning RFC 7807 `503 Service Unavailable` (`AI_COPILOT_UNAVAILABLE`) so the React UI gracefully falls back to the manual `OrderComposerPanel`.

### 2.5 Two-Tier Testing Strategy
- **Tier 1 — Deterministic CI Suite (Default)**: Unit and API integration tests inject a deterministic `FakeChatClient` returning recorded JSON fixtures (valid extraction, hallucinated SKU / prompt injection attempt, MOQ mismatch, and simulated `503` provider outage). Zero network calls, zero cost, millisecond execution.
- **Tier 2 — Live Model Verification**: Manual / on-demand live verification against Google Gemini (`gemini-2.5-flash`) and local Ollama (`llama3.2`) via configuration swap.

---

## 3. Consequences

- **Positive**: Zero vendor lock-in (`Gemini` ↔ `Ollama` config switch), immunity to LLM price fabrication, predictable token FinOps under partial outages, full OpenTelemetry `gen_ai` observability, and 100% deterministic CI tests.
- **Negative / Trade-offs**: Maintaining lightweight HTTP `IChatClient` adapters for Gemini `v1beta` and Ollama requires mapping wire DTOs in `TradeFlow.Infrastructure`, which we accept in exchange for full control over `HttpClient` Polly resilience, `x-goog-api-key` header hygiene, and native `responseJsonSchema` support.
