using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using TradeFlow.Application.AI.Exceptions;
using TradeFlow.Infrastructure.Modules.AI.Configuration;

namespace TradeFlow.Infrastructure.Modules.AI.Providers;

/// <summary>
/// Provider-agnostic <see cref="IChatClient"/> adapter for local Ollama inference (<c>/api/chat</c>).
/// Enables zero-code swapping between cloud Google Gemini and local open-weights models (e.g., <c>llama3.2</c>, <c>qwen2.5</c>)
/// simply by setting <c>AI:Provider=Ollama</c> (ADR-0020).
/// </summary>
public sealed class OllamaChatClient : IChatClient
{
    private static readonly JsonSerializerOptions WireJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly OllamaProviderOptions _options;
    private readonly ChatClientMetadata _metadata;

    public OllamaChatClient(HttpClient httpClient, OllamaProviderOptions options)
    {
        _httpClient = httpClient;
        _options = options;
        _metadata = new ChatClientMetadata(
            providerName: "Ollama",
            defaultModelId: options.Model,
            providerUri: new Uri(options.BaseUrl));
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var model = options?.ModelId ?? _options.Model;
        var endpoint = $"{_options.BaseUrl.TrimEnd('/')}/api/chat";
        var payload = BuildRequestPayload(model, messages, options, stream: false);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(endpoint, payload, WireJsonOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AiProviderException(
                "AI_OLLAMA_UNREACHABLE",
                $"Could not connect to local Ollama server at '{_options.BaseUrl}'. Ensure Ollama is running ('ollama serve' and 'ollama pull {model}').",
                ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new AiProviderException(
                    "AI_UPSTREAM_ERROR",
                    $"Ollama returned HTTP {(int)response.StatusCode}: {err}");
            }

            var ollamaResponse = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(WireJsonOptions, cancellationToken);
            var text = ollamaResponse?.Message?.Content ?? string.Empty;

            var result = new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
            {
                ModelId = ollamaResponse?.Model ?? model
            };

            if (ollamaResponse is not null)
            {
                var inputTokens = ollamaResponse.PromptEvalCount ?? 0;
                var outputTokens = ollamaResponse.EvalCount ?? 0;
                result.Usage = new UsageDetails
                {
                    InputTokenCount = inputTokens,
                    OutputTokenCount = outputTokens,
                    TotalTokenCount = inputTokens + outputTokens
                };
            }

            return result;
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var model = options?.ModelId ?? _options.Model;
        var endpoint = $"{_options.BaseUrl.TrimEnd('/')}/api/chat";
        var payload = BuildRequestPayload(model, messages, options, stream: true);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload, options: WireJsonOptions)
        };

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new AiProviderException(
                "AI_UPSTREAM_ERROR",
                $"Ollama streaming returned HTTP {(int)response.StatusCode}: {err}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!cancellationToken.IsCancellationRequested &&
               await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var chunk = JsonSerializer.Deserialize<OllamaChatResponse>(line, WireJsonOptions);
            var text = chunk?.Message?.Content;
            if (!string.IsNullOrEmpty(text))
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, text)
                {
                    ModelId = chunk?.Model ?? model
                };
            }

            if (chunk?.Done == true)
            {
                yield break;
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is not null)
        {
            return null;
        }

        if (serviceType == typeof(ChatClientMetadata))
        {
            return _metadata;
        }

        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
        // HttpClient lifetime is managed by IHttpClientFactory.
    }

    private static OllamaChatRequest BuildRequestPayload(
        string model,
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        bool stream)
    {
        var wireMessages = messages
            .Select(m => new OllamaMessage(
                Role: m.Role == ChatRole.System
                    ? "system"
                    : m.Role == ChatRole.Assistant ? "assistant" : "user",
                Content: m.Text))
            .ToList();

        object? format = null;
        if (options?.ResponseFormat is ChatResponseFormatJson jsonFormat)
        {
            format = jsonFormat.Schema is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } schemaElement
                ? schemaElement
                : "json";
        }

        var modelOptions = options?.Temperature.HasValue == true
            ? new OllamaModelOptions(options.Temperature.Value)
            : null;

        return new OllamaChatRequest(model, wireMessages, stream, format, modelOptions);
    }

    private sealed record OllamaChatRequest(
        string Model,
        IReadOnlyList<OllamaMessage> Messages,
        bool Stream,
        object? Format,
        OllamaModelOptions? Options);

    private sealed record OllamaMessage(string Role, string Content);

    private sealed record OllamaModelOptions(float Temperature);

    private sealed record OllamaChatResponse(
        string? Model,
        OllamaMessage? Message,
        bool Done,
        [property: JsonPropertyName("prompt_eval_count")] long? PromptEvalCount,
        [property: JsonPropertyName("eval_count")] long? EvalCount);
}
