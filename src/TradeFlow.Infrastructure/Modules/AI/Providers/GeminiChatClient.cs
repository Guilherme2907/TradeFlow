using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using TradeFlow.Application.AI.Exceptions;
using TradeFlow.Infrastructure.Modules.AI.Configuration;

namespace TradeFlow.Infrastructure.Modules.AI.Providers;

/// <summary>
/// Provider-agnostic <see cref="IChatClient"/> adapter for Google Gemini REST API (<c>v1beta</c>).
/// Supports native constrained decoding (<c>responseJsonSchema</c>), SSE token streaming,
/// token usage reporting, and <c>x-goog-api-key</c> header hygiene (ADR-0020).
/// </summary>
public sealed class GeminiChatClient : IChatClient
{
    private static readonly JsonSerializerOptions WireJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly GeminiProviderOptions _options;
    private readonly ChatClientMetadata _metadata;

    public GeminiChatClient(HttpClient httpClient, GeminiProviderOptions options)
    {
        _httpClient = httpClient;
        _options = options;
        _metadata = new ChatClientMetadata(
            providerName: "GoogleGemini",
            defaultModelId: options.Model,
            providerUri: new Uri(options.BaseUrl));
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var apiKey = ResolveApiKey();
        var model = options?.ModelId ?? _options.Model;
        var endpoint = $"{_options.BaseUrl.TrimEnd('/')}/models/{model}:generateContent";

        var payload = BuildRequestPayload(messages, options);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload, options: WireJsonOptions)
        };
        request.Headers.Add("x-goog-api-key", apiKey);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessOrThrowAsync(response, cancellationToken);

        var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiGenerateContentResponse>(WireJsonOptions, cancellationToken);
        var text = ExtractCandidateText(geminiResponse);

        var chatMessage = new ChatMessage(ChatRole.Assistant, text);
        var result = new ChatResponse(chatMessage)
        {
            ModelId = geminiResponse?.ModelVersion ?? model
        };

        if (geminiResponse?.UsageMetadata is { } usage)
        {
            result.Usage = new UsageDetails
            {
                InputTokenCount = usage.PromptTokenCount,
                OutputTokenCount = usage.CandidatesTokenCount,
                TotalTokenCount = usage.TotalTokenCount ?? (usage.PromptTokenCount + usage.CandidatesTokenCount)
            };
        }

        return result;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var apiKey = ResolveApiKey();
        var model = options?.ModelId ?? _options.Model;
        var endpoint = $"{_options.BaseUrl.TrimEnd('/')}/models/{model}:streamGenerateContent?alt=sse";

        var payload = BuildRequestPayload(messages, options);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload, options: WireJsonOptions)
        };
        request.Headers.Add("x-goog-api-key", apiKey);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessOrThrowAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!cancellationToken.IsCancellationRequested &&
               await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var jsonChunk = line["data:".Length..].Trim();
            if (string.IsNullOrEmpty(jsonChunk) || jsonChunk == "[DONE]")
            {
                continue;
            }

            var chunk = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(jsonChunk, WireJsonOptions);
            var text = ExtractCandidateText(chunk);
            if (!string.IsNullOrEmpty(text))
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, text)
                {
                    ModelId = chunk?.ModelVersion ?? model
                };
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

    private string ResolveApiKey()
    {
        var apiKey = !string.IsNullOrWhiteSpace(_options.ApiKey)
            ? _options.ApiKey
            : Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new AiProviderException(
                "AI_PROVIDER_NOT_CONFIGURED",
                "Google Gemini API key is not configured. Run 'dotnet user-secrets set \"AI:Gemini:ApiKey\" \"<YOUR_KEY>\" --project src/TradeFlow.Api' or set GEMINI_API_KEY, or switch 'AI:Provider' to 'Ollama'.");
        }

        return apiKey.Trim();
    }

    private static GeminiGenerateContentRequest BuildRequestPayload(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options)
    {
        var systemBuilder = new StringBuilder();
        var contents = new List<GeminiContent>();

        foreach (var msg in messages)
        {
            if (msg.Role == ChatRole.System)
            {
                if (!string.IsNullOrWhiteSpace(msg.Text))
                {
                    if (systemBuilder.Length > 0)
                    {
                        systemBuilder.AppendLine();
                    }

                    systemBuilder.Append(msg.Text);
                }

                continue;
            }

            var role = msg.Role == ChatRole.Assistant ? "model" : "user";
            contents.Add(new GeminiContent(role, [new GeminiPart(msg.Text)]));
        }

        if (contents.Count == 0)
        {
            contents.Add(new GeminiContent("user", [new GeminiPart(string.Empty)]));
        }

        GeminiContent? systemInstruction = systemBuilder.Length > 0
            ? new GeminiContent(null, [new GeminiPart(systemBuilder.ToString())])
            : null;

        string? responseMimeType = null;
        JsonNode? responseJsonSchema = null;

        if (options?.ResponseFormat is ChatResponseFormatJson jsonFormat)
        {
            responseMimeType = "application/json";
            if (jsonFormat.Schema is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } schemaElement)
            {
                responseJsonSchema = SanitizeJsonSchema(schemaElement);
            }
        }

        var generationConfig = new GeminiGenerationConfig(
            Temperature: options?.Temperature,
            MaxOutputTokens: options?.MaxOutputTokens,
            ResponseMimeType: responseMimeType,
            ResponseJsonSchema: responseJsonSchema);

        return new GeminiGenerateContentRequest(systemInstruction, contents, generationConfig);
    }

    private static JsonNode? SanitizeJsonSchema(JsonElement schemaElement)
    {
        var node = JsonNode.Parse(schemaElement.GetRawText());
        if (node is JsonObject obj)
        {
            obj.Remove("$schema");
        }

        return node;
    }

    private static string ExtractCandidateText(GeminiGenerateContentResponse? response)
    {
        var parts = response?.Candidates?.FirstOrDefault()?.Content?.Parts;
        if (parts is null || parts.Count == 0)
        {
            return string.Empty;
        }

        return string.Concat(parts.Select(p => p.Text));
    }

    private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new AiProviderException(
                "AI_RATE_LIMITED",
                "Google Gemini rate limit exceeded (HTTP 429). Please wait a few seconds or use the manual Order Composer.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new AiProviderException(
            "AI_UPSTREAM_ERROR",
            $"Google Gemini returned HTTP {(int)response.StatusCode}. Details: {Truncate(body, 280)}");
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "empty response";
        }

        return value.Length <= maxLength ? value : $"{value[..maxLength]}...";
    }

    private sealed record GeminiGenerateContentRequest(
        GeminiContent? SystemInstruction,
        IReadOnlyList<GeminiContent> Contents,
        GeminiGenerationConfig GenerationConfig);

    private sealed record GeminiContent(
        string? Role,
        IReadOnlyList<GeminiPart> Parts);

    private sealed record GeminiPart(string? Text);

    private sealed record GeminiGenerationConfig(
        float? Temperature,
        int? MaxOutputTokens,
        string? ResponseMimeType,
        JsonNode? ResponseJsonSchema);

    private sealed record GeminiGenerateContentResponse(
        IReadOnlyList<GeminiCandidate>? Candidates,
        GeminiUsageMetadata? UsageMetadata,
        string? ModelVersion);

    private sealed record GeminiCandidate(GeminiContent? Content);

    private sealed record GeminiUsageMetadata(
        long PromptTokenCount,
        long CandidatesTokenCount,
        long? TotalTokenCount);
}
