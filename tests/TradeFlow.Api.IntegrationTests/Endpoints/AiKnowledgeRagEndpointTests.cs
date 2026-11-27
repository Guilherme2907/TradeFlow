using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TradeFlow.Api.Endpoints;
using TradeFlow.Api.IntegrationTests.Infrastructure;
using TradeFlow.Application.AI.Gateways;
using TradeFlow.Application.AI.Rag;
using TradeFlow.Application.Common.Security;
using Xunit;

namespace TradeFlow.Api.IntegrationTests.Endpoints;

public class AiKnowledgeRagEndpointTests
{
    private static WebApplicationFactory<Program> CreateRagApiFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", $"TradeFlow_RagApi_{Guid.NewGuid()}");
            builder.UseSetting("ConnectionStrings:RabbitMq", string.Empty);
            builder.UseSetting("ConnectionStrings:Kafka", string.Empty);
            builder.UseSetting("ConnectionStrings:Redis", string.Empty);
            builder.UseSetting("Outbox:BackgroundWorkerEnabled", "false");
            builder.UseSetting("Authentication:Authority", TestJwtTokenGenerator.Issuer);
            builder.UseSetting("Authentication:Audience", TestJwtTokenGenerator.Audience);
            builder.UseSetting("Authentication:RequireHttpsMetadata", "false");

            builder.ConfigureServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = null;
                    options.MetadataAddress = null!;
                    options.TokenValidationParameters.ValidIssuer = TestJwtTokenGenerator.Issuer;
                    options.TokenValidationParameters.ValidAudiences = [TestJwtTokenGenerator.Audience, "account"];
                    options.TokenValidationParameters.IssuerSigningKey = TestJwtTokenGenerator.SigningKey;
                    options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                });

                services.RemoveAll<IEmbeddingGenerator<string, Embedding<float>>>();
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, KeywordHashEmbeddingGenerator>();

                services.RemoveAll<IChatClient>();
                services.AddSingleton<IChatClient, EchoGroundedCitationsChatClient>();
            });
        });
    }

    [Fact]
    public async Task KnowledgeEndpoints_ShouldPreventCrossTenantContractLeakageAndEnforceAdminReindexPolicy()
    {
        await using var factory = CreateRagApiFactory();
        using var client = factory.CreateClient();

        var alphaTenantId = Guid.NewGuid();
        var betaTenantId = Guid.NewGuid();

        var adminToken = TestJwtTokenGenerator.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), TradeFlowRoles.Admin);
        var alphaTraderToken = TestJwtTokenGenerator.GenerateToken(Guid.NewGuid(), alphaTenantId, TradeFlowRoles.Trader);
        var betaTraderToken = TestJwtTokenGenerator.GenerateToken(Guid.NewGuid(), betaTenantId, TradeFlowRoles.Trader);

        // 1. Seed public Catalog spec + Alpha confidential contract (6.5%) + Beta confidential contract (11.5%)
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var createProd = await client.PostAsJsonAsync("/api/catalog/products", new CreateProductRequest(
            "SKU-STAINLESS-004",
            "Stainless Steel 316L Marine Plate",
            "Ferrous Metals",
            "ASTM A240 chloride pitting resistance for offshore rigs.",
            4120m,
            "USD",
            "MT",
            1));
        createProd.StatusCode.Should().Be(HttpStatusCode.Created);

        var createAlpha = await client.PostAsJsonAsync("/api/customers", new CreateCustomerAccountRequest(
            "Alpha Trading Corp", "US-ALPHA-1", "Enterprise", 500_000m, "USD", 45, alphaTenantId));
        createAlpha.StatusCode.Should().Be(HttpStatusCode.Created);

        var createAlphaContract = await client.PostAsJsonAsync($"/api/customers/{alphaTenantId}/contracts", new AddCustomerContractRequest(
            "MSA-ALPHA-01",
            "Alpha Rebate Agreement",
            "Alpha confidential rebate of 6.5% on Copper and Steel.",
            6.5m,
            DateTimeOffset.UtcNow.AddDays(-5),
            DateTimeOffset.UtcNow.AddYears(1)));
        createAlphaContract.StatusCode.Should().Be(HttpStatusCode.Created);

        var createBeta = await client.PostAsJsonAsync("/api/customers", new CreateCustomerAccountRequest(
            "Beta Metals Corp", "DE-BETA-2", "Gold", 900_000m, "EUR", 60, betaTenantId));
        createBeta.StatusCode.Should().Be(HttpStatusCode.Created);

        var createBetaContract = await client.PostAsJsonAsync($"/api/customers/{betaTenantId}/contracts", new AddCustomerContractRequest(
            "MSA-BETA-99",
            "Beta Secret Strategic Rebate",
            "Beta confidential strategic rebate of 11.5% on all alloys.",
            11.5m,
            DateTimeOffset.UtcNow.AddDays(-5),
            DateTimeOffset.UtcNow.AddYears(1)));
        createBetaContract.StatusCode.Should().Be(HttpStatusCode.Created);

        // 2. Trader cannot call POST /api/ai/knowledge/reindex -> 403 Forbidden; Admin succeeds -> 200 OK
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", alphaTraderToken);
        var forbiddenReindex = await client.PostAsJsonAsync("/api/ai/knowledge/reindex", new { });
        forbiddenReindex.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var okReindex = await client.PostAsJsonAsync("/api/ai/knowledge/reindex", new { });
        okReindex.StatusCode.Should().Be(HttpStatusCode.OK);
        var stats = await okReindex.Content.ReadFromJsonAsync<KnowledgeReindexResponse>();
        stats!.CatalogProductsIndexed.Should().Be(1);
        stats.CustomerContractsIndexed.Should().Be(2);

        // 3. Trader Alpha attacks by asking directly about Beta Metals' secret contract ("MSA-BETA-99 Beta Metals")
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", alphaTraderToken);
        var alphaAttackResp = await client.PostAsJsonAsync(
            "/api/ai/knowledge/ask",
            new AskKnowledgeRequest("What is the secret strategic rebate in MSA-BETA-99 for Beta Metals Corp?", TopK: 5));
        alphaAttackResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var alphaAnswer = await alphaAttackResp.Content.ReadFromJsonAsync<KnowledgeAnswerResponse>();
        alphaAnswer!.Citations.Should().NotContain(c => c.SourceId == "MSA-BETA-99",
            "pre-filtering must strip Beta Metals' confidential chunks before Top-K ranking");
        alphaAnswer.Citations.Should().OnlyContain(c => c.CustomerId == null || c.CustomerId == alphaTenantId);

        // 4. Trader Beta asks the exact same question -> receives own MSA-BETA-99 confidential contract!
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", betaTraderToken);
        var betaResp = await client.PostAsJsonAsync(
            "/api/ai/knowledge/ask",
            new AskKnowledgeRequest("What is the secret strategic rebate in MSA-BETA-99 for Beta Metals Corp?", TopK: 5));
        betaResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var betaAnswer = await betaResp.Content.ReadFromJsonAsync<KnowledgeAnswerResponse>();
        betaAnswer!.Citations.Should().Contain(c => c.SourceId == "MSA-BETA-99" && c.IsTenantConfidential);
    }

    private sealed class KeywordHashEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly EmbeddingGeneratorMetadata _meta = new("TestEmbedder", null, "hash-768", KnowledgeDimensions.VectorSize);

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var list = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var text in values)
            {
                var vec = new float[KnowledgeDimensions.VectorSize];
                foreach (var word in text.ToLowerInvariant().Split([' ', '-', '.', ',', ':', '[', ']'], StringSplitOptions.RemoveEmptyEntries))
                {
                    var hash = BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(word)), 0);
                    vec[hash % KnowledgeDimensions.VectorSize] += 1.0f;
                }

                list.Add(new Embedding<float>(vec) { ModelId = "hash-768" });
            }

            return Task.FromResult(list);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType == typeof(EmbeddingGeneratorMetadata) ? _meta : null;

        public void Dispose() { }
    }

    private sealed class EchoGroundedCitationsChatClient : IChatClient
    {
        private readonly ChatClientMetadata _meta = new("RecordedTestAI", null, "grounded-v1");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var sys = messages.FirstOrDefault(m => m.Role == ChatRole.System)?.Text ?? string.Empty;
            var hasBeta = sys.Contains("MSA-BETA-99", StringComparison.OrdinalIgnoreCase);
            var json = hasBeta
                ? """{"answer":"Beta discount is 11.5% [MSA-BETA-99].","citedSourceIds":["MSA-BETA-99"],"hasSufficientEvidence":true}"""
                : """{"answer":"No authorized contract found for Beta Metals.","citedSourceIds":[],"hasSufficientEvidence":false}""";

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json))
            {
                ModelId = "grounded-v1",
                Usage = new UsageDetails { InputTokenCount = 250, OutputTokenCount = 40, TotalTokenCount = 290 }
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType == typeof(ChatClientMetadata) ? _meta : null;

        public void Dispose() { }
    }
}
