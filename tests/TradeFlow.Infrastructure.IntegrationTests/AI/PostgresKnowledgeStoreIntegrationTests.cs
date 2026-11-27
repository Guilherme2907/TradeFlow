using FluentAssertions;
using Testcontainers.PostgreSql;
using TradeFlow.Application.AI.Gateways;
using TradeFlow.Infrastructure.Modules.AI.Persistence;
using Xunit;

namespace TradeFlow.Infrastructure.IntegrationTests.AI;

[Trait("Category", "RequiresDocker")]
public sealed class PostgresKnowledgeStoreIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .WithDatabase("tradeflow_pgvector_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public Task InitializeAsync() => _postgresContainer.StartAsync();

    public Task DisposeAsync() => _postgresContainer.DisposeAsync().AsTask();

    [Fact]
    public async Task SearchHybridAsync_ShouldEnforceTenantPreFiltering_FuseVectorAndLexicalRrf_AndIsolateEmbeddingModels()
    {
        var store = new PostgresKnowledgeStore(_postgresContainer.GetConnectionString());

        var alphaTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var betaTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        // Axis 0 = Marine Alloy concept; Axis 1 = Rebate Contract concept; Axis 2 = Exact Code fallback
        var vecMarine = CreateBasisVector(axis: 0);
        var vecContract = CreateBasisVector(axis: 1);
        var vecOrthogonal = CreateBasisVector(axis: 2);

        const string ollamaModel = "Ollama:nomic-embed-text";
        const string geminiModel = "GoogleGemini:gemini-embedding-001";

        await store.ReplaceAllForModelAsync(
            ollamaModel,
            [
                // 1. Global Catalog Specification (CustomerId = null)
                new KnowledgeChunkToUpsert(
                    Guid.NewGuid(),
                    "CatalogProduct",
                    "SKU-STAINLESS-004",
                    0,
                    CustomerId: null,
                    "SKU-STAINLESS-004 — Stainless Steel 316L Marine Plate",
                    "ASTM A240 molybdenum-alloyed stainless steel for offshore rigs and chemical reactors.",
                    ollamaModel,
                    vecMarine),

                // 2. Alpha Confidential Contract (CustomerId = alphaTenantId)
                new KnowledgeChunkToUpsert(
                    Guid.NewGuid(),
                    "CustomerContract",
                    "MSA-ALPHA-2026-01",
                    0,
                    CustomerId: alphaTenantId,
                    "MSA-ALPHA-2026-01 — Alpha Rebate Agreement",
                    "Alpha Trading confidential rebate of 6.5% and Net 45 payment terms.",
                    ollamaModel,
                    vecContract),

                // 3. Beta Confidential Contract with IDENTICAL semantic vector (CustomerId = betaTenantId)
                // Even if Beta's vector has 1.000 cosine similarity, Alpha MUST NEVER retrieve it!
                new KnowledgeChunkToUpsert(
                    Guid.NewGuid(),
                    "CustomerContract",
                    "MSA-BETA-SECRET-99",
                    0,
                    CustomerId: betaTenantId,
                    "MSA-BETA-SECRET-99 — Beta Strategic Agreement",
                    "Beta Metals confidential strategic rebate of 12.0% and EUR 900k credit line.",
                    ollamaModel,
                    vecContract),

                // 4. Lexical-only exact code match (orthogonal vector, but exact ASTM code in text)
                new KnowledgeChunkToUpsert(
                    Guid.NewGuid(),
                    "CatalogProduct",
                    "SKU-TITAN-008",
                    0,
                    CustomerId: null,
                    "SKU-TITAN-008 — Titanium Bar Grade 5",
                    "Certified under specification ASTM-B348-SPECIAL for aerospace turbine shafts.",
                    ollamaModel,
                    vecOrthogonal)
            ]);

        // Also store a chunk under Gemini embedding space to prove model-space isolation
        await store.ReplaceAllForModelAsync(
            geminiModel,
            [
                new KnowledgeChunkToUpsert(
                    Guid.NewGuid(),
                    "CatalogProduct",
                    "GEMINI-ONLY-CHUNK",
                    0,
                    CustomerId: null,
                    "Gemini Space Chunk",
                    "Stored under GoogleGemini embedding space.",
                    geminiModel,
                    vecContract)
            ]);

        (await store.CountChunksAsync(ollamaModel)).Should().Be(4);
        (await store.CountChunksAsync(geminiModel)).Should().Be(1);

        // Act 1: Trader Alpha queries with vecContract -> MUST get MSA-ALPHA-2026-01 + public catalog, NEVER MSA-BETA-SECRET-99 or GEMINI-ONLY-CHUNK!
        var alphaResults = await store.SearchHybridAsync(new KnowledgeSearchQuery(
            QueryText: "rebate contract",
            QueryEmbedding: vecContract,
            EmbeddingModel: ollamaModel,
            AllowedCustomerId: alphaTenantId,
            AllowCrossTenant: false,
            TopK: 5));

        alphaResults.Should().NotBeEmpty();
        alphaResults[0].SourceId.Should().Be("MSA-ALPHA-2026-01");
        alphaResults[0].CosineSimilarity.Should().BeApproximately(1.0, 0.001);
        alphaResults[0].MatchedVector.Should().BeTrue();
        alphaResults[0].MatchedLexical.Should().BeTrue();

        alphaResults.Should().NotContain(c => c.SourceId == "MSA-BETA-SECRET-99",
            "PostgreSQL SQL pre-filtering must prevent cross-tenant leakage before vector ranking");
        alphaResults.Should().NotContain(c => c.SourceId == "GEMINI-ONLY-CHUNK",
            "embedding_model partitioning must prevent mixing incompatible vector spaces");

        // Act 2: Hybrid Lexical Rescue — query vector points to Marine (axis 0), but text asks for exact code "ASTM-B348-SPECIAL"
        var hybridResults = await store.SearchHybridAsync(new KnowledgeSearchQuery(
            QueryText: "ASTM-B348-SPECIAL",
            QueryEmbedding: vecMarine,
            EmbeddingModel: ollamaModel,
            AllowedCustomerId: alphaTenantId,
            AllowCrossTenant: false,
            TopK: 5));

        hybridResults.Should().Contain(c => c.SourceId == "SKU-TITAN-008" && c.MatchedLexical);
        hybridResults.Should().Contain(c => c.SourceId == "SKU-STAINLESS-004" && c.MatchedVector);
    }

    private static float[] CreateBasisVector(int axis)
    {
        var vec = new float[KnowledgeDimensions.VectorSize];
        vec[axis] = 1.0f;
        return vec;
    }
}
