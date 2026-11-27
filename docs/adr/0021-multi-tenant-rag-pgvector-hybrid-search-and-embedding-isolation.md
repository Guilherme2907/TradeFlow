# ADR-0021: Multi-Tenant RAG Architecture with PostgreSQL `pgvector`, Hybrid Search (RRF), and Embedding Space Isolation

- **Status**: Accepted
- **Date**: 2026-11-25
- **Deciders**: Architecture & Engineering Mentorship Team
- **Phase**: Phase 12 — Applied AI Engineering (`Stage 12.2`)

---

## 1. Context and Problem Statement

As TradeFlow's catalog specifications (`Catalog` module) and corporate B2B commercial agreements (`Customers` module) grow, injecting the entire corpus into every LLM system prompt becomes untenable due to token costs, latency, context-window degradation (*Lost in the Middle*), and severe cross-tenant confidentiality hazards (`ADR-0013`).

Building a production Retrieval-Augmented Generation (RAG) subsystem inside our Modular Monolith presents four critical engineering challenges:

1. **Cross-Tenant Data Leakage & Top-K Starvation**: Performing unconstrained `Top-K` vector similarity search across all tenants and filtering afterward in C# (*Post-Filtering*) risks leaking confidential contract terms if prompt filtering is trusted, or returning `0` chunks (*Recall Collapse*) when another tenant dominates the nearest neighbors.
2. **Pure Vector Search Blindness on Exact Codes**: Dense vector embeddings excel at cross-language conceptual similarity (e.g., *"metal para plataforma offshore"* -> `Stainless Steel 316L`), but underperform on exact alphanumeric identifiers (`MSA-ALPHA-2026-01`, `ASTM A240`, `SKU-TITAN-008`).
3. **Incompatible Embedding Spaces Across Providers**: Because TradeFlow supports runtime provider switching (`Gemini` <-> `Ollama` per `ADR-0020`), comparing query vectors produced by one embedding model against document vectors produced by another produces silent semantic garbage, even when vector dimensions (`768`) match.
4. **Module Boundary Preservation (`ADR-0007`)**: The AI module must not couple directly to `catalog.products` or `customers.customer_contracts` SQL tables.

---

## 2. Decision Outcome

### 2.1 Dedicated `ai` Schema on PostgreSQL `pgvector` (`AiDbContext`)
- Rather than introducing an external vector database (eliminating dual-write operational overhead), we standardize on **PostgreSQL 16 + `pgvector`** (`pgvector/pgvector:pg16`) inside an isolated `ai` schema (`ai.knowledge_chunks`), managed via `AiDbContext` (`IModuleDbContext`).
- Ingestion reads domain data strictly through explicit in-process contracts (`ICatalogModule` and `ICustomersModule`), prepending **Contextual Chunk Headers** (`[SourceType | SourceId | Tenant/Scope]`) and splitting text on sentence/paragraph boundaries with word overlap.

### 2.2 Mandatory SQL Pre-Filtering (`customer_id`)
- Every row in `ai.knowledge_chunks` carries a nullable `customer_id`:
  - `customer_id IS NULL`: platform-wide public knowledge (e.g., `Catalog` technical specs).
  - `customer_id = <TenantGuid>`: confidential tenant documents (e.g., `Customers` B2B contracts and credit terms).
- For non-privileged callers (`Trader`), `WHERE (customer_id IS NULL OR customer_id = @CurrentCustomerId)` is enforced **inside the PostgreSQL query prior to Top-K ranking**, guaranteeing both zero cross-tenant leakage and full `Top-K` recall for the caller's tenant.

### 2.3 Hybrid Search: `pgvector` Cosine (`HNSW`) + PostgreSQL Full-Text (`tsvector`) + RRF
- Retrieval executes both:
  1. **Semantic Search**: `embedding <=> @queryVector::vector(768)` accelerated by an `HNSW` index (`vector_cosine_ops`).
  2. **Lexical Search**: `to_tsvector('simple', title || ' ' || content) @@ websearch_to_tsquery('simple', @queryText)` accelerated by a `GIN` index.
- Candidates from both rankings are fused via **Reciprocal Rank Fusion (`RRF`, $k = 60$)**:
  $$\text{RRF}(d) = \frac{1}{60 + \text{rank}_{\text{vector}}(d)} + \frac{1}{60 + \text{rank}_{\text{lexical}}(d)}$$

### 2.4 Embedding Space Partitioning (`embedding_model`)
- Both `GeminiEmbeddingGenerator` (`gemini-embedding-001` with `outputDimensionality = 768`) and `OllamaEmbeddingGenerator` (`nomic-embed-text`, native `768` dimensions) implement `Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>>`.
- Every chunk persists `embedding_model` (e.g., `Ollama:nomic-embed-text` or `GoogleGemini:gemini-embedding-001`), and queries strictly filter `AND embedding_model = @ActiveEmbeddingModel`. When switching providers, missing embeddings for the active model are indexed automatically without corrupting or mixing incompatible vector spaces.

### 2.5 Grounded Synthesis & Citations
- `AskKnowledgeQuestionQueryHandler` instructs `IChatClient` via structured JSON Schema (`LlmGroundedRagAnswer`) to answer **solely** from retrieved chunks, cite exact `SourceId`s, and set `hasSufficientEvidence = false` when retrieved context cannot answer the question.

---

## 3. Consequences

- **Positive**: Zero extra infrastructure beyond PostgreSQL, exact SKU/contract-number + cross-language semantic recall via Hybrid RRF, provable multi-tenant isolation tested against real `pgvector` in Testcontainers, and seamless `Gemini` <-> `Ollama` provider switching.
- **Negative / Trade-offs**: Switching `AI:Provider` triggers a one-time re-embedding pass for the newly selected `embedding_model`, which is fast for thousands of chunks and guarantees mathematical validity of vector comparisons.
