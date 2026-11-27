namespace TradeFlow.Application.AI.Gateways;

public static class KnowledgeDimensions
{
    public const int VectorSize = 768;
}

public sealed record KnowledgeChunkToUpsert(
    Guid Id,
    string SourceType,
    string SourceId,
    int ChunkIndex,
    Guid? CustomerId,
    string Title,
    string Content,
    string EmbeddingModel,
    ReadOnlyMemory<float> Embedding);

public sealed record KnowledgeSearchQuery(
    string QueryText,
    ReadOnlyMemory<float> QueryEmbedding,
    string EmbeddingModel,
    Guid? AllowedCustomerId,
    bool AllowCrossTenant,
    int TopK = 5);

public sealed record RetrievedKnowledgeChunk(
    Guid ChunkId,
    string SourceType,
    string SourceId,
    int ChunkIndex,
    Guid? CustomerId,
    string Title,
    string Content,
    double CosineSimilarity,
    double RrfScore,
    bool MatchedVector,
    bool MatchedLexical);

/// <summary>
/// Port for storing and querying multi-tenant hybrid knowledge chunks (pgvector HNSW + PostgreSQL Full-Text RRF).
/// Enforces tenant pre-filtering and embedding-space partitioning (ADR-0021).
/// </summary>
public interface IKnowledgeStore
{
    Task<int> CountChunksAsync(string embeddingModel, CancellationToken cancellationToken = default);

    Task ReplaceAllForModelAsync(
        string embeddingModel,
        IReadOnlyList<KnowledgeChunkToUpsert> chunks,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RetrievedKnowledgeChunk>> SearchHybridAsync(
        KnowledgeSearchQuery query,
        CancellationToken cancellationToken = default);
}
