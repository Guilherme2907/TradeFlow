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
/// Local Ollama <c>/api/embed</c> adapter implementing <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>
/// (default model: <c>nomic-embed-text</c>, 768 dimensions) (ADR-0021).
/// </summary>
public sealed class OllamaEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private static readonly JsonSerializerOptions WireJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly OllamaProviderOptions _options;
    private readonly EmbeddingGeneratorMetadata _metadata;

    public OllamaEmbeddingGenerator(HttpClient httpClient, OllamaProviderOptions options)
    {
        _httpClient = httpClient;
        _options = options;
        _metadata = new EmbeddingGeneratorMetadata(
            providerName: "Ollama",
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

        var model = options?.ModelId ?? _options.EmbeddingModel;
        var dimensions = options?.Dimensions ?? KnowledgeDimensions.VectorSize;
        var endpoint = $"{_options.BaseUrl.TrimEnd('/')}/api/embed";

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(
                endpoint,
                new OllamaEmbedRequest(model, inputs),
                WireJsonOptions,
                cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AiProviderException(
                "AI_OLLAMA_UNREACHABLE",
                $"Could not connect to Ollama at '{_options.BaseUrl}' for embeddings. Ensure 'ollama serve' is running.",
                ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new AiProviderException(
                    "AI_EMBEDDING_MODEL_NOT_FOUND",
                    $"Ollama embedding model '{model}' is not installed locally. Run 'ollama pull {model}' in your terminal and retry.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new AiProviderException(
                    "AI_EMBEDDING_UPSTREAM_ERROR",
                    $"Ollama embedding endpoint returned HTTP {(int)response.StatusCode}: {err}");
            }

            var parsed = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(WireJsonOptions, cancellationToken);
            var embeddings = new GeneratedEmbeddings<Embedding<float>>();

            foreach (var rawVec in parsed?.Embeddings ?? [])
            {
                var normalized = VectorMath.NormalizeAndResize(rawVec, dimensions);
                embeddings.Add(new Embedding<float>(normalized) { ModelId = parsed?.Model ?? model });
            }

            return embeddings;
        }
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

    private sealed record OllamaEmbedRequest(string Model, IReadOnlyList<string> Input);

    private sealed record OllamaEmbedResponse(string? Model, IReadOnlyList<float[]>? Embeddings);
}
