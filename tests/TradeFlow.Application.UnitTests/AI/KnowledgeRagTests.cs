using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TradeFlow.Application.AI.Gateways;
using TradeFlow.Application.AI.Queries.AskKnowledgeQuestion;
using TradeFlow.Application.AI.Rag;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Common.Security;
using Xunit;

namespace TradeFlow.Application.UnitTests.AI;

public class KnowledgeRagTests
{
    [Fact]
    public void SemanticTextChunker_LongDocument_ShouldPreserveHeaderAndSentenceOverlap()
    {
        const string header = "[Confidential B2B Contract | SourceId: MSA-ALPHA-01 | Tenant: Alpha Corp]";
        var body = string.Join(' ', Enumerable.Range(1, 12)
            .Select(i => $"Clause {i} establishes commercial obligation number {i} for maritime metal delivery and rebate compliance."));

        var chunks = SemanticTextChunker.ChunkWithContextualHeader(header, body, targetWordsPerChunk: 45, overlapWords: 15);

        chunks.Count.Should().BeGreaterThan(2);
        chunks.Should().OnlyContain(c => c.StartsWith(header));

        // Verify sliding sentence overlap between consecutive chunks
        chunks[1].Should().Contain("Clause 3");
        chunks[0].Should().Contain("Clause 3");
    }

    [Fact]
    public async Task AskKnowledgeQuestion_TraderAlpha_ShouldAutoIndexMissingModelAndPreFilterByTenant()
    {
        var alphaTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var user = Substitute.For<ICurrentUserContext>();
        user.IsAuthenticated.Returns(true);
        user.CustomerId.Returns(alphaTenantId);
        user.IsInRole(TradeFlowRoles.Trader).Returns(true);

        var indexer = Substitute.For<IKnowledgeBaseIndexer>();
        indexer.GetActiveEmbeddingModelKey().Returns("Ollama:nomic-embed-text");
        indexer.ReindexAllAsync(Arg.Any<CancellationToken>())
            .Returns(new KnowledgeReindexResponse("Ollama:nomic-embed-text", 10, 2, 14, 120));

        KnowledgeSearchQuery? capturedQuery = null;
        var store = Substitute.For<IKnowledgeStore>();
        store.CountChunksAsync("Ollama:nomic-embed-text", Arg.Any<CancellationToken>()).Returns(0);
        store.SearchHybridAsync(Arg.Do<KnowledgeSearchQuery>(q => capturedQuery = q), Arg.Any<CancellationToken>())
            .Returns(
            [
                new RetrievedKnowledgeChunk(
                    Guid.NewGuid(),
                    "CustomerContract",
                    "MSA-ALPHA-2026-01",
                    0,
                    alphaTenantId,
                    "MSA-ALPHA-2026-01 — Alpha Rebate Agreement",
                    "[Header]\nNegotiated Rebate: 6.50%.",
                    0.912,
                    0.0325,
                    true,
                    true)
            ]);

        var embedder = new DeterministicTestEmbeddingGenerator();
        var chat = new DeterministicRagChatClient(
            """
            {
              "answer": "Seu desconto contratual acordado é de 6,50% conforme [MSA-ALPHA-2026-01].",
              "citedSourceIds": ["MSA-ALPHA-2026-01"],
              "hasSufficientEvidence": true
            }
            """);

        var handler = new AskKnowledgeQuestionQueryHandler(
            indexer,
            store,
            embedder,
            chat,
            user,
            NullLogger<AskKnowledgeQuestionQueryHandler>.Instance);

        var result = await handler.Handle(
            new AskKnowledgeQuestionQuery("Qual é o nosso desconto de rebate?"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AutoIndexedOnDemand.Should().BeTrue();
        result.Value.HasSufficientEvidence.Should().BeTrue();
        result.Value.Citations.Should().ContainSingle(c =>
            c.SourceId == "MSA-ALPHA-2026-01" &&
            c.CitedByModel &&
            c.IsTenantConfidential);

        await indexer.Received(1).ReindexAllAsync(Arg.Any<CancellationToken>());
        capturedQuery.Should().NotBeNull();
        capturedQuery!.AllowedCustomerId.Should().Be(alphaTenantId);
        capturedQuery.AllowCrossTenant.Should().BeFalse();
        capturedQuery.EmbeddingModel.Should().Be("Ollama:nomic-embed-text");
    }

    [Fact]
    public async Task AskKnowledgeQuestion_TraderPassingOtherTenantFilter_ShouldReturnForbidden()
    {
        var alphaTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var betaTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var user = Substitute.For<ICurrentUserContext>();
        user.IsAuthenticated.Returns(true);
        user.CustomerId.Returns(alphaTenantId);
        user.IsInRole(TradeFlowRoles.Trader).Returns(true);

        var handler = new AskKnowledgeQuestionQueryHandler(
            Substitute.For<IKnowledgeBaseIndexer>(),
            Substitute.For<IKnowledgeStore>(),
            new DeterministicTestEmbeddingGenerator(),
            new DeterministicRagChatClient("{}"),
            user,
            NullLogger<AskKnowledgeQuestionQueryHandler>.Instance);

        var result = await handler.Handle(
            new AskKnowledgeQuestionQuery("Mostre o contrato", FilterCustomerId: betaTenantId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be("RAG_TENANT_FORBIDDEN");
    }

    private sealed class DeterministicTestEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly EmbeddingGeneratorMetadata _meta = new("Ollama", null, "nomic-embed-text", KnowledgeDimensions.VectorSize);

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var result = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var _ in values)
            {
                var vec = new float[KnowledgeDimensions.VectorSize];
                vec[0] = 1.0f;
                result.Add(new Embedding<float>(vec) { ModelId = "nomic-embed-text" });
            }

            return Task.FromResult(result);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType == typeof(EmbeddingGeneratorMetadata) ? _meta : null;

        public void Dispose() { }
    }

    private sealed class DeterministicRagChatClient(string json) : IChatClient
    {
        private readonly ChatClientMetadata _meta = new("Ollama", null, "llama3.2");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json))
            {
                ModelId = "llama3.2",
                Usage = new UsageDetails { InputTokenCount = 350, OutputTokenCount = 60, TotalTokenCount = 410 }
            });

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, json);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType == typeof(ChatClientMetadata) ? _meta : null;

        public void Dispose() { }
    }
}
