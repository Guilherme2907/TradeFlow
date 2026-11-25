namespace TradeFlow.Application.AI.Exceptions;

/// <summary>
/// Raised by <c>IChatClient</c> infrastructure adapters when an upstream LLM provider is unconfigured,
/// rate-limited after retries, circuit-broken, or returns a non-recoverable upstream error (ADR-0020).
/// </summary>
public sealed class AiProviderException : Exception
{
    public string ErrorCode { get; }

    public AiProviderException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
