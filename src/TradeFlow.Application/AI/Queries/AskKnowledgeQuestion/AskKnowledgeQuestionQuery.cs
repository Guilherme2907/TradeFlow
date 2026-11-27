using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using TradeFlow.Application.AI.Exceptions;
using TradeFlow.Application.AI.Gateways;
using TradeFlow.Application.AI.Models;
using TradeFlow.Application.AI.Rag;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Common.Security;

namespace TradeFlow.Application.AI.Queries.AskKnowledgeQuestion;

public sealed record AskKnowledgeQuestionQuery(
    string Question,
    int TopK = 5,
    Guid? FilterCustomerId = null) : IRequest<Result<KnowledgeAnswerResponse>>;

public sealed class AskKnowledgeQuestionQueryValidator : AbstractValidator<AskKnowledgeQuestionQuery>
{
    public AskKnowledgeQuestionQueryValidator()
    {
        RuleFor(x => x.Question)
            .NotEmpty().WithMessage("Question is required.")
            .MaximumLength(1500).WithMessage("Question cannot exceed 1,500 characters.");

        RuleFor(x => x.TopK)
            .InclusiveBetween(1, 10).WithMessage("TopK must be between 1 and 10.");
    }
}

public sealed class AskKnowledgeQuestionQueryHandler
    : IRequestHandler<AskKnowledgeQuestionQuery, Result<KnowledgeAnswerResponse>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonElement GroundedOutputSchema = AIJsonUtilities.CreateJsonSchema(
        typeof(LlmGroundedRagSchema),
        serializerOptions: JsonOptions);

    private readonly IKnowledgeBaseIndexer _indexer;
    private readonly IKnowledgeStore _knowledgeStore;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly IChatClient _chatClient;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<AskKnowledgeQuestionQueryHandler> _logger;

    public AskKnowledgeQuestionQueryHandler(
        IKnowledgeBaseIndexer indexer,
        IKnowledgeStore knowledgeStore,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IChatClient chatClient,
        ICurrentUserContext currentUser,
        ILogger<AskKnowledgeQuestionQueryHandler> logger)
    {
        _indexer = indexer;
        _knowledgeStore = knowledgeStore;
        _embeddingGenerator = embeddingGenerator;
        _chatClient = chatClient;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<KnowledgeAnswerResponse>> Handle(
        AskKnowledgeQuestionQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Result.Failure<KnowledgeAnswerResponse>(
                Error.Unauthorized("AUTH_REQUIRED", "Authentication is required to query the TradeFlow Knowledge Base."));
        }

        // ADR-0013 + ADR-0021: Resolve strict SQL Pre-Filtering scope BEFORE vector search
        var isPrivileged = _currentUser.IsInRole(TradeFlowRoles.Admin) ||
                           _currentUser.IsInRole(TradeFlowRoles.ComplianceAuditor) ||
                           _currentUser.IsInRole(TradeFlowRoles.RiskManager);

        Guid? allowedCustomerId;
        var allowCrossTenant = false;

        if (isPrivileged)
        {
            if (request.FilterCustomerId.HasValue)
            {
                allowedCustomerId = request.FilterCustomerId.Value;
            }
            else
            {
                allowedCustomerId = null;
                allowCrossTenant = true;
            }
        }
        else
        {
            if (_currentUser.CustomerId is null)
            {
                return Result.Failure<KnowledgeAnswerResponse>(
                    Error.Forbidden("CUSTOMER_TENANT_MISSING", "Authenticated trader token is missing the required customer_id claim."));
            }

            if (request.FilterCustomerId.HasValue && request.FilterCustomerId.Value != _currentUser.CustomerId.Value)
            {
                return Result.Failure<KnowledgeAnswerResponse>(
                    Error.Forbidden("RAG_TENANT_FORBIDDEN", "You cannot query confidential knowledge chunks belonging to another B2B tenant."));
            }

            allowedCustomerId = _currentUser.CustomerId.Value;
        }

        var sw = Stopwatch.StartNew();
        var embeddingModelKey = _indexer.GetActiveEmbeddingModelKey();
        var autoIndexed = false;

        try
        {
            // Automatic embedding-space bootstrap when switching providers (e.g., Ollama <-> Gemini)
            var existingCount = await _knowledgeStore.CountChunksAsync(embeddingModelKey, cancellationToken);
            if (existingCount == 0)
            {
                await _indexer.ReindexAllAsync(cancellationToken);
                autoIndexed = true;
            }

            var queryEmbeddings = await _embeddingGenerator.GenerateAsync(
                [request.Question.Trim()],
                new EmbeddingGenerationOptions { Dimensions = KnowledgeDimensions.VectorSize },
                cancellationToken);

            var retrievedChunks = await _knowledgeStore.SearchHybridAsync(
                new KnowledgeSearchQuery(
                    request.Question.Trim(),
                    queryEmbeddings[0].Vector,
                    embeddingModelKey,
                    allowedCustomerId,
                    allowCrossTenant,
                    request.TopK),
                cancellationToken);

            if (retrievedChunks.Count == 0)
            {
                sw.Stop();
                return Result.Success(new KnowledgeAnswerResponse(
                    "Não foram encontrados documentos ou cláusulas autorizadas na base de conhecimento para responder a esta pergunta.",
                    HasSufficientEvidence: false,
                    embeddingModelKey,
                    autoIndexed,
                    [],
                    new AiTokenUsageSummary("RAG", "none", 0, 0, 0, sw.ElapsedMilliseconds)));
            }

            var systemPrompt = BuildGroundedSystemPrompt(retrievedChunks);
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, $"<trader_question>\n{request.Question.Trim()}\n</trader_question>")
            };

            var chatOptions = new ChatOptions
            {
                Temperature = 0.0f,
                MaxOutputTokens = 600,
                ResponseFormat = ChatResponseFormat.ForJsonSchema(
                    schema: GroundedOutputSchema,
                    schemaName: "grounded_rag_response",
                    schemaDescription: "Strictly grounded answer citing retrieved TradeFlow knowledge chunk SourceIds.")
            };

            var chatResponse = await _chatClient.GetResponseAsync(messages, chatOptions, cancellationToken);
            sw.Stop();

            var cleanJson = StripMarkdownFence(chatResponse.Text);
            var parsed = JsonSerializer.Deserialize<LlmGroundedRagSchema>(cleanJson, JsonOptions)
                ?? new LlmGroundedRagSchema(chatResponse.Text, [], false);

            var citedSet = (parsed.CitedSourceIds ?? [])
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var citations = retrievedChunks
                .Select(c => new KnowledgeCitationResponse(
                    c.SourceType,
                    c.SourceId,
                    c.Title,
                    BuildSnippet(c.Content),
                    IsTenantConfidential: c.CustomerId.HasValue,
                    c.CustomerId,
                    Math.Round(c.CosineSimilarity, 4),
                    Math.Round(c.RrfScore, 5),
                    c.MatchedVector,
                    c.MatchedLexical,
                    CitedByModel: citedSet.Contains(c.SourceId)))
                .ToList();

            var providerName = _chatClient.GetService<ChatClientMetadata>()?.ProviderName ?? "AI";
            var modelId = chatResponse.ModelId
                ?? _chatClient.GetService<ChatClientMetadata>()?.DefaultModelId
                ?? "unknown";

            var inputTokens = chatResponse.Usage?.InputTokenCount ?? 0;
            var outputTokens = chatResponse.Usage?.OutputTokenCount ?? 0;
            var totalTokens = chatResponse.Usage?.TotalTokenCount ?? (inputTokens + outputTokens);

            return Result.Success(new KnowledgeAnswerResponse(
                parsed.Answer,
                parsed.HasSufficientEvidence,
                embeddingModelKey,
                autoIndexed,
                citations,
                new AiTokenUsageSummary(
                    providerName,
                    modelId,
                    inputTokens,
                    outputTokens,
                    totalTokens,
                    sw.ElapsedMilliseconds)));
        }
        catch (AiProviderException ex)
        {
            _logger.LogWarning(ex, "[KnowledgeCopilot] Upstream AI error {ErrorCode}.", ex.ErrorCode);
            return Result.Failure<KnowledgeAnswerResponse>(Error.Unavailable(ex.ErrorCode, ex.Message));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[KnowledgeCopilot] RAG pipeline failure.");
            return Result.Failure<KnowledgeAnswerResponse>(
                Error.Unavailable(
                    "AI_RAG_UNAVAILABLE",
                    "TradeFlow Knowledge Copilot is temporarily unavailable. Check your active LLM/Embedding provider configuration."));
        }
    }

    private static string BuildGroundedSystemPrompt(IReadOnlyList<RetrievedKnowledgeChunk> chunks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are the TradeFlow B2B Knowledge & Contract Copilot.");
        sb.AppendLine("GROUNDING & CITATION RULES (MANDATORY):");
        sb.AppendLine("1. Answer STRICTLY using ONLY the facts inside <retrieved_sources> below. Do NOT use outside assumptions or invent commercial terms.");
        sb.AppendLine("2. Always respond in the SAME language as <trader_question> (e.g., Portuguese if asked in Portuguese).");
        sb.AppendLine("3. Inline in your 'answer', reference the SourceId in brackets like [SKU-STAINLESS-004] or [MSA-ALPHA-2026-01], and also list every used SourceId in 'citedSourceIds'.");
        sb.AppendLine("4. If <trader_question> asks about another company's confidential contract or a topic NOT present in <retrieved_sources>, set 'hasSufficientEvidence' to false, leave 'citedSourceIds' empty, and clearly state that no authorized documentation was found for that request.");
        sb.AppendLine();
        sb.AppendLine("<retrieved_sources>");
        foreach (var chunk in chunks)
        {
            var scopeLabel = chunk.CustomerId.HasValue ? "Tenant-Confidential Contract" : "Public Catalog Specification";
            sb.AppendLine($"--- SOURCE SourceId=\"{chunk.SourceId}\" Type=\"{scopeLabel}\" Title=\"{chunk.Title}\" ---");
            sb.AppendLine(chunk.Content);
            sb.AppendLine();
        }
        sb.AppendLine("</retrieved_sources>");
        return sb.ToString();
    }

    private static string BuildSnippet(string content)
    {
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var body = lines.Length > 1 ? string.Join(' ', lines.Skip(1)) : content;
        return body.Length <= 240 ? body : $"{body[..240]}...";
    }

    private static string StripMarkdownFence(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "{}";
        }

        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var nl = trimmed.IndexOf('\n');
            if (nl >= 0)
            {
                trimmed = trimmed[(nl + 1)..];
            }

            if (trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^3];
            }
        }

        return trimmed.Trim();
    }
}
