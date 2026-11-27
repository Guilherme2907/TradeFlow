using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using TradeFlow.Application.AI.Exceptions;
using TradeFlow.Application.AI.Gateways;
using TradeFlow.Infrastructure.Modules.AI.Configuration;

namespace TradeFlow.Infrastructure.Modules.AI.Providers;

/// <summary>
/// Google Gemini <c>v1beta</c> batch embedding adapter implementing <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>.
/// Requests Matryoshka 768-dimensional vectors and L2-normalizes them for exact cosine similarity (ADR-0021).
/// </summary>
public sealed class GeminiEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private static readonly JsonSerializerOptions WireJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly GeminiProviderOptions _options;
    private readonly EmbeddingGeneratorMetadata _metadata;

    public GeminiEmbeddingGenerator(HttpClient httpClient, GeminiProviderOptions options)
    {
        _httpClient = httpClient;
        _options = options;
        _metadata = new EmbeddingGeneratorMetadata(
            providerName: "GoogleGemini",
            providerUri: new Uri(options.BaseUrl),
            defaultModelId: options.EmbeddingModel,
            defaultModelDimensions: KnowledgeDimensions.VectorSize);
    }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var inputs = values.ToList();
        if (inputs.Count == 0)
        {
            return [];
        }

        var apiKey = !string.IsNullOrWhiteSpace(_options.ApiKey)
            ? _options.ApiKey.Trim()
            : Environment.GetEnvironmentVariable("GEMINI_API_KEY")?.Trim();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new AiProviderException(
                "AI_PROVIDER_NOT_CONFIGURED",
                "Google Gemini API key is not configured for embeddings. Configure 'AI:Gemini:ApiKey' or switch 'AI:Provider' to 'Ollama'.");
        }

        var model = options?.ModelId ?? _options.EmbeddingModel;
        var dimensions = options?.Dimensions ?? KnowledgeDimensions.VectorSize;
        var endpoint = $"{_options.BaseUrl.TrimEnd('/')}/models/{model}:batchEmbedContents";

        var batchPayload = new GeminiBatchEmbedRequest(
            inputs.Select(text => new GeminiEmbedSingleRequest(
                $"models/{model}",
                new GeminiEmbedContent([new GeminiEmbedPart(text)]),
                dimensions)).ToList());

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(batchPayload, options: WireJsonOptions)
        };
        request.Headers.Add("x-goog-api-key", apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new AiProviderException("AI_RATE_LIMITED", "Google Gemini embedding rate limit exceeded (HTTP 429).");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new AiProviderException(
                "AI_EMBEDDING_UPSTREAM_ERROR",
                $"Google Gemini embedding endpoint returned HTTP {(int)response.StatusCode}: {body}");
        }

        var parsed = await response.Content.ReadFromJsonAsync<GeminiBatchEmbedResponse>(WireJsonOptions, cancellationToken);
        var embeddings = new GeneratedEmbeddings<Embedding<float>>();

        foreach (var item in parsed?.Embeddings ?? [])
        {
            var normalized = VectorMath.NormalizeAndResize(item.Values ?? [], dimensions);
            embeddings.Add(new Embedding<float>(normalized) { ModelId = model });
        }

        return embeddings;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is not null)
        {
            return null;
        }

        if (serviceType == typeof(EmbeddingGeneratorMetadata))
        {
            return _metadata;
        }

        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
    }

    private sealed record GeminiBatchEmbedRequest(IReadOnlyList<GeminiEmbedSingleRequest> Requests);

    private sealed record GeminiEmbedSingleRequest(
        string Model,
        GeminiEmbedContent Content,
        int OutputDimensionality);

    private sealed record GeminiEmbedContent(IReadOnlyList<GeminiEmbedPart> Parts);

    private sealed record GeminiEmbedPart(string Text);

    private sealed record GeminiBatchEmbedResponse(IReadOnlyList<GeminiContentEmbedding>? Embeddings);

    private sealed record GeminiContentEmbedding(float[]? Values);
}
