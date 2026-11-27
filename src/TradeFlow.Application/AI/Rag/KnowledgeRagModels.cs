using System.ComponentModel;
using TradeFlow.Application.AI.Models;

namespace TradeFlow.Application.AI.Rag;

public sealed record LlmGroundedRagSchema(
    [property: Description("Direct, well-structured answer in the SAME language as the user's question, grounded strictly in the provided knowledge chunks. If the chunks do not contain the answer, state clearly that no documented evidence was found.")]
    string Answer,
    [property: Description("Exact SourceId codes (e.g., SKU-STAINLESS-004 or MSA-ALPHA-2026-01) from the retrieved chunks that directly support the answer.")]
    IReadOnlyList<string> CitedSourceIds,
    [property: Description("True ONLY if the retrieved chunks contain sufficient factual evidence to answer the user's question; false if the information is missing or belongs to an unavailable source.")]
    bool HasSufficientEvidence);

public sealed record KnowledgeCitationResponse(
    string SourceType,
    string SourceId,
    string Title,
    string Snippet,
    bool IsTenantConfidential,
    Guid? CustomerId,
    double CosineSimilarity,
    double RrfScore,
    bool MatchedVector,
    bool MatchedLexical,
    bool CitedByModel);

public sealed record KnowledgeAnswerResponse(
    string Answer,
    bool HasSufficientEvidence,
    string EmbeddingModel,
    bool AutoIndexedOnDemand,
    IReadOnlyList<KnowledgeCitationResponse> Citations,
    AiTokenUsageSummary Telemetry);

public sealed record KnowledgeReindexResponse(
    string EmbeddingModel,
    int CatalogProductsIndexed,
    int CustomerContractsIndexed,
    int TotalChunksStored,
    long ElapsedMs);
