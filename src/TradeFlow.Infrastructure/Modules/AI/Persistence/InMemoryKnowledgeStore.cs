using System.Collections.Concurrent;
using TradeFlow.Application.AI.Gateways;
using TradeFlow.Infrastructure.Modules.AI.Providers;

namespace TradeFlow.Infrastructure.Modules.AI.Persistence;

/// <summary>
/// Lightweight in-memory <see cref="IKnowledgeStore"/> used strictly when <c>UseInMemoryDatabase=true</c>
/// in fast non-Docker tests. Implements identical tenant pre-filtering and RRF math as <see cref="PostgresKnowledgeStore"/>.
/// </summary>
public sealed class InMemoryKnowledgeStore : IKnowledgeStore
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<KnowledgeChunkToUpsert>> _byModel = new(StringComparer.OrdinalIgnoreCase);

    public Task<int> CountChunksAsync(string embeddingModel, CancellationToken cancellationToken = default)
    {
        var count = _byModel.TryGetValue(embeddingModel, out var list) ? list.Count : 0;
        return Task.FromResult(count);
    }

    public Task ReplaceAllForModelAsync(
        string embeddingModel,
        IReadOnlyList<KnowledgeChunkToUpsert> chunks,
        CancellationToken cancellationToken = default)
    {
        _byModel[embeddingModel] = chunks.ToList();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RetrievedKnowledgeChunk>> SearchHybridAsync(
        KnowledgeSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!_byModel.TryGetValue(query.EmbeddingModel, out var all) || all.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<RetrievedKnowledgeChunk>>([]);
        }

        var filtered = all
            .Where(c => query.AllowCrossTenant ||
                        c.CustomerId is null ||
                        (query.AllowedCustomerId.HasValue && c.CustomerId == query.AllowedCustomerId.Value))
            .ToList();

        if (filtered.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<RetrievedKnowledgeChunk>>([]);
        }

        var vectorRanked = filtered
            .Select(c => (Chunk: c, Sim: VectorMath.CosineSimilarity(c.Embedding.Span, query.QueryEmbedding.Span)))
            .OrderByDescending(x => x.Sim)
            .Take(20)
            .Select((x, idx) => (x.Chunk, x.Sim, Rank: idx + 1))
            .ToDictionary(x => x.Chunk.Id);

        var terms = (query.QueryText ?? string.Empty)
            .Split([' ', '\t', '\r', '\n', ',', '.', '?', '!', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2)
            .ToArray();

        var lexicalRanked = filtered
            .Select(c =>
            {
                var haystack = $"{c.Title} {c.Content}";
                var hits = terms.Count(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase));
                return (Chunk: c, Hits: hits);
            })
            .Where(x => x.Hits > 0)
            .OrderByDescending(x => x.Hits)
            .Take(20)
            .Select((x, idx) => (x.Chunk, Rank: idx + 1))
            .ToDictionary(x => x.Chunk.Id);

        var candidateIds = vectorRanked.Keys.Union(lexicalRanked.Keys);
        var results = candidateIds
            .Select(id =>
            {
                var hasVec = vectorRanked.TryGetValue(id, out var v);
                var hasLex = lexicalRanked.TryGetValue(id, out var l);
                var chunk = hasVec ? v.Chunk : l.Chunk;
                var rrf = (hasVec ? 1.0 / (60.0 + v.Rank) : 0.0) + (hasLex ? 1.0 / (60.0 + l.Rank) : 0.0);

                return new RetrievedKnowledgeChunk(
                    chunk.Id,
                    chunk.SourceType,
                    chunk.SourceId,
                    chunk.ChunkIndex,
                    chunk.CustomerId,
                    chunk.Title,
                    chunk.Content,
                    hasVec ? v.Sim : 0.0,
                    rrf,
                    hasVec,
                    hasLex);
            })
            .OrderByDescending(r => r.RrfScore)
            .ThenByDescending(r => r.CosineSimilarity)
            .Take(Math.Clamp(query.TopK, 1, 20))
            .ToList();

        return Task.FromResult<IReadOnlyList<RetrievedKnowledgeChunk>>(results);
    }
}
