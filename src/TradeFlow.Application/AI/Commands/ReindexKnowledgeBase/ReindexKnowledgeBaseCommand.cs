using MediatR;
using Microsoft.Extensions.Logging;
using TradeFlow.Application.AI.Exceptions;
using TradeFlow.Application.AI.Rag;
using TradeFlow.Application.Common.Models;

namespace TradeFlow.Application.AI.Commands.ReindexKnowledgeBase;

public sealed record ReindexKnowledgeBaseCommand : IRequest<Result<KnowledgeReindexResponse>>;

public sealed class ReindexKnowledgeBaseCommandHandler
    : IRequestHandler<ReindexKnowledgeBaseCommand, Result<KnowledgeReindexResponse>>
{
    private readonly IKnowledgeBaseIndexer _indexer;
    private readonly ILogger<ReindexKnowledgeBaseCommandHandler> _logger;

    public ReindexKnowledgeBaseCommandHandler(
        IKnowledgeBaseIndexer indexer,
        ILogger<ReindexKnowledgeBaseCommandHandler> logger)
    {
        _indexer = indexer;
        _logger = logger;
    }

    public async Task<Result<KnowledgeReindexResponse>> Handle(
        ReindexKnowledgeBaseCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var summary = await _indexer.ReindexAllAsync(cancellationToken);
            return Result.Success(summary);
        }
        catch (AiProviderException ex)
        {
            _logger.LogWarning(ex, "[KnowledgeReindex] Embedding provider error {ErrorCode}.", ex.ErrorCode);
            return Result.Failure<KnowledgeReindexResponse>(Error.Unavailable(ex.ErrorCode, ex.Message));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[KnowledgeReindex] Failed to reindex knowledge base.");
            return Result.Failure<KnowledgeReindexResponse>(
                Error.Unavailable("AI_INDEXING_UNAVAILABLE", "Knowledge base indexing is temporarily unavailable."));
        }
    }
}
