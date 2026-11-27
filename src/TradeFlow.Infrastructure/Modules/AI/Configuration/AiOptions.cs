namespace TradeFlow.Infrastructure.Modules.AI.Configuration;

public sealed class AiOptions
{
    public const string SectionName = "AI";

    /// <summary>
    /// Active LLM provider: "Gemini" (default) or "Ollama".
    /// </summary>
    public string Provider { get; set; } = "Ollama";

    public GeminiProviderOptions Gemini { get; set; } = new();

    public OllamaProviderOptions Ollama { get; set; } = new();

    public AiResilienceOptions Resilience { get; set; } = new();
}

public sealed class GeminiProviderOptions
{
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    public string Model { get; set; } = "gemini-3.8-flash";

    public string EmbeddingModel { get; set; } = "gemini-embedding-001";

    /// <summary>
    /// Google AI Studio API key. Can also be supplied via the <c>GEMINI_API_KEY</c> environment variable.
    /// </summary>
    public string? ApiKey { get; set; }
}

public sealed class OllamaProviderOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";

    public string Model { get; set; } = "llama3.2";

    public string EmbeddingModel { get; set; } = "nomic-embed-text";
}

public sealed class AiResilienceOptions
{
    public double AttemptTimeoutSeconds { get; set; } = 25.0;

    public double TotalTimeoutSeconds { get; set; } = 65.0;

    public int MaxRetries { get; set; } = 2;

    public int RetryBaseDelayMs { get; set; } = 1000;

    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    public int CircuitBreakerMinimumThroughput { get; set; } = 5;

    public double CircuitBreakerSamplingSeconds { get; set; } = 30.0;

    public double CircuitBreakerBreakSeconds { get; set; } = 30.0;
}
