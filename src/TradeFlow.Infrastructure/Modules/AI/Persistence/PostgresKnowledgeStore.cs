using Npgsql;
using NpgsqlTypes;
using TradeFlow.Application.AI.Gateways;
using TradeFlow.Infrastructure.Modules.AI.Providers;

namespace TradeFlow.Infrastructure.Modules.AI.Persistence;

/// <summary>
/// Production PostgreSQL + <c>pgvector</c> implementation of <see cref="IKnowledgeStore"/> (ADR-0021).
/// Stores chunks inside the isolated <c>ai.knowledge_chunks</c> schema with an HNSW cosine index + GIN Full-Text index,
/// executing mandatory SQL <c>customer_id</c> pre-filtering and Reciprocal Rank Fusion (RRF) hybrid retrieval.
/// </summary>
public sealed class PostgresKnowledgeStore : IKnowledgeStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _initialized;

    public PostgresKnowledgeStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<int> CountChunksAsync(string embeddingModel, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*)::int FROM ai.knowledge_chunks WHERE embedding_model = @embeddingModel;",
            conn);
        cmd.Parameters.AddWithValue("embeddingModel", embeddingModel);

        var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
        return scalar is int count ? count : Convert.ToInt32(scalar);
    }

    public async Task ReplaceAllForModelAsync(
        string embeddingModel,
        IReadOnlyList<KnowledgeChunkToUpsert> chunks,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        await using (var deleteCmd = new NpgsqlCommand(
            "DELETE FROM ai.knowledge_chunks WHERE embedding_model = @embeddingModel;",
            conn,
            tx))
        {
            deleteCmd.Parameters.AddWithValue("embeddingModel", embeddingModel);
            await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertSql = """
            INSERT INTO ai.knowledge_chunks (
                id,
                source_type,
                source_id,
                chunk_index,
                customer_id,
                title,
                content,
                embedding_model,
                embedding,
                indexed_at
            )
            VALUES (
                @id,
                @sourceType,
                @sourceId,
                @chunkIndex,
                @customerId,
                @title,
                @content,
                @embeddingModel,
                CAST(@embeddingLiteral AS public.vector(768)),
                NOW()
            );
            """;

        foreach (var chunk in chunks)
        {
            var normalized = VectorMath.NormalizeAndResize(chunk.Embedding.Span, KnowledgeDimensions.VectorSize);
            var vectorLiteral = VectorMath.FormatPgVectorLiteral(normalized);

            await using var cmd = new NpgsqlCommand(insertSql, conn, tx);
            cmd.Parameters.AddWithValue("id", chunk.Id);
            cmd.Parameters.AddWithValue("sourceType", chunk.SourceType);
            cmd.Parameters.AddWithValue("sourceId", chunk.SourceId);
            cmd.Parameters.AddWithValue("chunkIndex", chunk.ChunkIndex);
            cmd.Parameters.Add(new NpgsqlParameter("customerId", NpgsqlDbType.Uuid)
            {
                Value = chunk.CustomerId.HasValue ? chunk.CustomerId.Value : DBNull.Value
            });
            cmd.Parameters.AddWithValue("title", chunk.Title);
            cmd.Parameters.AddWithValue("content", chunk.Content);
            cmd.Parameters.AddWithValue("embeddingModel", chunk.EmbeddingModel);
            cmd.Parameters.AddWithValue("embeddingLiteral", vectorLiteral);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RetrievedKnowledgeChunk>> SearchHybridAsync(
        KnowledgeSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaInitializedAsync(cancellationToken);

        var normalizedQueryVec = VectorMath.NormalizeAndResize(query.QueryEmbedding.Span, KnowledgeDimensions.VectorSize);
        var queryVectorLiteral = VectorMath.FormatPgVectorLiteral(normalizedQueryVec);

        const string selectSql = """
            WITH filtered AS (
                SELECT id, source_type, source_id, chunk_index, customer_id, title, content, embedding
                FROM ai.knowledge_chunks
                WHERE embedding_model = @embeddingModel
                  AND (@allowCrossTenant = TRUE
                       OR customer_id IS NULL
                       OR (@allowedCustomerId IS NOT NULL AND customer_id = @allowedCustomerId))
            ),
            vector_ranked AS (
                SELECT id,
                       (1.0 - (embedding <=> CAST(@queryVector AS public.vector(768)))) AS cosine_similarity,
                       ROW_NUMBER() OVER (ORDER BY embedding <=> CAST(@queryVector AS public.vector(768)) ASC) AS vec_rank
                FROM filtered
                ORDER BY embedding <=> CAST(@queryVector AS public.vector(768)) ASC
                LIMIT 20
            ),
            lexical_ranked AS (
                SELECT id,
                       ROW_NUMBER() OVER (
                           ORDER BY ts_rank_cd(
                               to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(content, '')),
                               replace(plainto_tsquery('simple', @queryText)::text, ' & ', ' | ')::tsquery
                           ) DESC
                       ) AS lex_rank
                FROM filtered
                WHERE plainto_tsquery('simple', @queryText)::text <> ''
                  AND to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(content, ''))
                      @@ replace(plainto_tsquery('simple', @queryText)::text, ' & ', ' | ')::tsquery
                LIMIT 20
            ),
            fused AS (
                SELECT COALESCE(v.id, l.id) AS id,
                       COALESCE(v.cosine_similarity, 0.0) AS cosine_similarity,
                       (COALESCE(1.0 / (60.0 + v.vec_rank), 0.0) + COALESCE(1.0 / (60.0 + l.lex_rank), 0.0)) AS rrf_score,
                       (v.id IS NOT NULL) AS matched_vector,
                       (l.id IS NOT NULL) AS matched_lexical
                FROM vector_ranked v
                FULL OUTER JOIN lexical_ranked l ON v.id = l.id
            )
            SELECT f.id,
                   c.source_type,
                   c.source_id,
                   c.chunk_index,
                   c.customer_id,
                   c.title,
                   c.content,
                   f.cosine_similarity,
                   f.rrf_score,
                   f.matched_vector,
                   f.matched_lexical
            FROM fused f
            INNER JOIN filtered c ON c.id = f.id
            ORDER BY f.rrf_score DESC, f.cosine_similarity DESC
            LIMIT @topK;
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        await using var setPathCmd = new NpgsqlCommand("SET LOCAL search_path TO public, ai;", conn, tx);
        await setPathCmd.ExecuteNonQueryAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(selectSql, conn, tx);
        cmd.Parameters.AddWithValue("embeddingModel", query.EmbeddingModel);
        cmd.Parameters.AddWithValue("allowCrossTenant", query.AllowCrossTenant);
        cmd.Parameters.Add(new NpgsqlParameter("allowedCustomerId", NpgsqlDbType.Uuid)
        {
            Value = query.AllowedCustomerId.HasValue ? query.AllowedCustomerId.Value : DBNull.Value
        });
        cmd.Parameters.AddWithValue("queryVector", queryVectorLiteral);
        cmd.Parameters.AddWithValue("queryText", query.QueryText ?? string.Empty);
        cmd.Parameters.AddWithValue("topK", Math.Clamp(query.TopK, 1, 20));

        var results = new List<RetrievedKnowledgeChunk>();
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new RetrievedKnowledgeChunk(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetDouble(7),
                    reader.GetDouble(8),
                    reader.GetBoolean(9),
                    reader.GetBoolean(10)));
            }
        }

        await tx.CommitAsync(cancellationToken);
        return results;
    }

    private async Task EnsureSchemaInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            const string ddl = """
                CREATE EXTENSION IF NOT EXISTS vector SCHEMA public;
                CREATE SCHEMA IF NOT EXISTS ai;

                CREATE TABLE IF NOT EXISTS ai.knowledge_chunks (
                    id uuid PRIMARY KEY,
                    source_type varchar(40) NOT NULL,
                    source_id varchar(100) NOT NULL,
                    chunk_index integer NOT NULL,
                    customer_id uuid NULL,
                    title varchar(260) NOT NULL,
                    content text NOT NULL,
                    embedding_model varchar(120) NOT NULL,
                    embedding public.vector(768) NOT NULL,
                    indexed_at timestamptz NOT NULL DEFAULT NOW()
                );

                CREATE INDEX IF NOT EXISTS ix_knowledge_chunks_tenant_model
                    ON ai.knowledge_chunks (embedding_model, customer_id);

                CREATE INDEX IF NOT EXISTS ix_knowledge_chunks_embedding_hnsw
                    ON ai.knowledge_chunks USING hnsw (embedding public.vector_cosine_ops);

                CREATE INDEX IF NOT EXISTS ix_knowledge_chunks_fts
                    ON ai.knowledge_chunks USING gin (to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(content, '')));
                """;

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(ddl, conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
