using System.ComponentModel;

namespace TradeFlow.Application.AI.Models;

/// <summary>
/// Untrusted intent schema populated by the LLM via constrained JSON Schema decoding (ADR-0020).
/// Intentionally omits UnitPrice, ProductId, and CustomerId so prompt injection or model hallucination
/// can never dictate commercial pricing or cross-tenant ownership.
/// </summary>
public sealed record LlmOrderExtractionSchema(
    [property: Description("3-letter ISO 4217 currency code requested by the trader (e.g., USD, EUR, BRL), or null if unspecified.")]
    string? RequestedCurrency,
    [property: Description("Optional minimum order value constraint mentioned by the trader, or null if not mentioned.")]
    decimal? RequestedMinimumOrderValue,
    [property: Description("Commodity line items matched to exact SKUs from the provided active catalog.")]
    IReadOnlyList<LlmExtractedLineItem> Items,
    [property: Description("Requested items or terms that could not be matched to any active catalog SKU.")]
    IReadOnlyList<string> UnmatchedRequests,
    [property: Description("Concise summary in the same language as the trader message explaining what was extracted.")]
    string AssistantSummary);

public sealed record LlmExtractedLineItem(
    [property: Description("Exact catalog SKU identifier from the system prompt (e.g., SKU-COPPER-A-001).")]
    string Sku,
    [property: Description("Positive integer quantity requested by the trader.")]
    int Quantity,
    [property: Description("Brief note explaining which phrase in the trader prompt mapped to this SKU.")]
    string? MatchNote);

/// <summary>
/// Catalog-verified line item enriched with authoritative pricing and MOQ metadata in deterministic C# code.
/// </summary>
public sealed record VerifiedDraftLineItem(
    Guid ProductId,
    string Sku,
    string ProductName,
    string Category,
    string UnitOfMeasure,
    decimal AuthoritativeUnitPrice,
    string CatalogCurrency,
    int Quantity,
    int MinimumOrderQuantity,
    decimal EstimatedLineTotal,
    string? MatchNote);

public sealed record AiTokenUsageSummary(
    string Provider,
    string ModelId,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    long LatencyMs);

public sealed record OrderDraftProposalResponse(
    string RecommendedCurrency,
    decimal? RecommendedMinimumOrderValue,
    decimal EstimatedTotalAmount,
    IReadOnlyList<VerifiedDraftLineItem> VerifiedItems,
    IReadOnlyList<string> UnmatchedRequests,
    IReadOnlyList<string> ValidationWarnings,
    string AssistantSummary,
    AiTokenUsageSummary Telemetry);
