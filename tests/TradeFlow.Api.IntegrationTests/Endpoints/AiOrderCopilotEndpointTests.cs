using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TradeFlow.Api.Endpoints;
using TradeFlow.Api.IntegrationTests.Infrastructure;
using TradeFlow.Application.AI.Models;
using TradeFlow.Application.Common.Security;
using Xunit;

namespace TradeFlow.Api.IntegrationTests.Endpoints;

public class AiOrderCopilotEndpointTests
{
    private static WebApplicationFactory<Program> CreateApiFactory(
        string provider = "Gemini",
        string? geminiApiKey = "",
        IChatClient? overrideChatClient = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", $"TradeFlow_AiTest_{Guid.NewGuid()}");
            builder.UseSetting("ConnectionStrings:RabbitMq", string.Empty);
            builder.UseSetting("ConnectionStrings:Kafka", string.Empty);
            builder.UseSetting("ConnectionStrings:Redis", string.Empty);
            builder.UseSetting("Outbox:BackgroundWorkerEnabled", "false");
            builder.UseSetting("AI:Provider", provider);
            builder.UseSetting("AI:Gemini:ApiKey", geminiApiKey);
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

                if (overrideChatClient is not null)
                {
                    services.RemoveAll<IChatClient>();
                    services.AddSingleton(overrideChatClient);
                }
            });
        });
    }

    [Fact]
    public async Task ParseDraft_ShouldEnforceRbacAndReturn503WhenGeminiKeyIsMissing()
    {
        var previousEnvKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);

        try
        {
            await using var factory = CreateApiFactory(provider: "Gemini", geminiApiKey: "");
            using var client = factory.CreateClient();

            // 1. Anonymous caller -> 401 Unauthorized
            var anonResp = await client.PostAsJsonAsync("/api/ai/orders/parse-draft", new ParseOrderDraftRequest("Buy 10 Copper"));
            anonResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            // 2. Read-only ComplianceAuditor -> 403 Forbidden (requires OrderWritePolicy)
            var auditorToken = TestJwtTokenGenerator.GenerateToken(Guid.NewGuid(), null, TradeFlowRoles.ComplianceAuditor);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auditorToken);
            var forbiddenResp = await client.PostAsJsonAsync("/api/ai/orders/parse-draft", new ParseOrderDraftRequest("Buy 10 Copper"));
            forbiddenResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            // 3. Admin creates an active product, then Trader calls Copilot with unconfigured Gemini key -> 503 Service Unavailable (Retry-After header)
            var adminToken = TestJwtTokenGenerator.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), TradeFlowRoles.Admin);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            var prodResp = await client.PostAsJsonAsync("/api/catalog/products", new CreateProductRequest(
                "SKU-COPPER-A-001", "Copper Grade A Cathode", "LME Copper", "Base Metals", 9150m, "USD", "MT", 5));
            prodResp.StatusCode.Should().Be(HttpStatusCode.Created);

            var traderToken = TestJwtTokenGenerator.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), TradeFlowRoles.Trader);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", traderToken);
            var unavailableResp = await client.PostAsJsonAsync("/api/ai/orders/parse-draft", new ParseOrderDraftRequest("Buy 10 Copper"));
            unavailableResp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            unavailableResp.Headers.RetryAfter.Should().NotBeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", previousEnvKey);
        }
    }

    [Fact]
    public async Task ParseDraftAndSseStream_WithRecordedChatClient_ShouldWorkEndToEnd()
    {
        const string recordedExtractionJson = """
            {
              "requestedCurrency": "USD",
              "requestedMinimumOrderValue": 40000,
              "items": [
                { "sku": "SKU-NICKEL-BRI-004", "quantity": 5, "matchNote": "5 MT Nickel Briquettes" }
              ],
              "unmatchedRequests": ["2 MT Vibranium (not in catalog)"],
              "assistantSummary": "Matched 5 MT of Class 1 Nickel Briquettes."
            }
            """;

        var recordedClient = new RecordedApiChatClient(
            recordedExtractionJson,
            ["Class 1 Nickel ", "is priced at ", "$16,800/MT."]);

        await using var factory = CreateApiFactory(overrideChatClient: recordedClient);
        using var client = factory.CreateClient();

        var adminToken = TestJwtTokenGenerator.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), TradeFlowRoles.Admin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var createProd = await client.PostAsJsonAsync("/api/catalog/products", new CreateProductRequest(
            "SKU-NICKEL-BRI-004", "Class 1 Nickel Briquettes", "99.8% Nickel", "Battery Metals", 16800m, "USD", "MT", 2));
        createProd.StatusCode.Should().Be(HttpStatusCode.Created);

        var traderToken = TestJwtTokenGenerator.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), TradeFlowRoles.Trader);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", traderToken);

        // 1. Structured Order Intake
        var parseResp = await client.PostAsJsonAsync(
            "/api/ai/orders/parse-draft",
            new ParseOrderDraftRequest("Need 5 MT of Nickel Briquettes and 2 MT Vibranium in USD, min 40k"));
        parseResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var proposal = await parseResp.Content.ReadFromJsonAsync<OrderDraftProposalResponse>();
        proposal!.VerifiedItems.Should().ContainSingle(x =>
            x.Sku == "SKU-NICKEL-BRI-004" &&
            x.AuthoritativeUnitPrice == 16800m &&
            x.EstimatedLineTotal == 84000m);
        proposal.UnmatchedRequests.Should().ContainSingle(x => x.Contains("Vibranium"));

        // 2. Real-time SSE Token Streaming
        var sseResp = await client.PostAsJsonAsync(
            "/api/ai/orders/stream-briefing",
            new StreamTradeBriefingRequest("Summarize Nickel pricing."));
        sseResp.StatusCode.Should().Be(HttpStatusCode.OK);
        sseResp.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        var rawSse = await sseResp.Content.ReadAsStringAsync();
        rawSse.Should().Contain("Class 1 Nickel ");
        rawSse.Should().Contain("\"type\":\"done\"");
    }

    [Fact]
    public async Task ProviderSwap_ShouldRegisterGeminiOrOllamaBehindSameIChatClientAbstraction()
    {
        await using var geminiFactory = CreateApiFactory(provider: "Gemini", geminiApiKey: "dummy-key");
        var geminiClient = geminiFactory.Services.GetRequiredService<IChatClient>();
        geminiClient.GetService<ChatClientMetadata>()!.ProviderName.Should().Be("GoogleGemini");

        await using var ollamaFactory = CreateApiFactory(provider: "Ollama");
        var ollamaClient = ollamaFactory.Services.GetRequiredService<IChatClient>();
        ollamaClient.GetService<ChatClientMetadata>()!.ProviderName.Should().Be("Ollama");
    }

    private sealed class RecordedApiChatClient(string jsonResponse, IReadOnlyList<string> streamChunks) : IChatClient
    {
        private readonly ChatClientMetadata _metadata = new("RecordedTestAI", providerUri: null, defaultModelId: "gemini-2.5-flash-recorded");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, jsonResponse))
            {
                ModelId = _metadata.DefaultModelId,
                Usage = new UsageDetails { InputTokenCount = 210, OutputTokenCount = 65, TotalTokenCount = 275 }
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var chunk in streamChunks)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
                await Task.Yield();
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType == typeof(ChatClientMetadata) ? _metadata : null;

        public void Dispose()
        {
        }
    }
}
