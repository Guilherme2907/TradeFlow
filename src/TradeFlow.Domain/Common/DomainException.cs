namespace TradeFlow.Domain.Common;

/// <summary>
/// Base exception for domain invariant violations and business rule breaches.
/// </summary>
public abstract class DomainException : Exception
{
    public string Code { get; }

    protected DomainException(string code, string message) : base(message)
    {
        Code = code;
    }
}
