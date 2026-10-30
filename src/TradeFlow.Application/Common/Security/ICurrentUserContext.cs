namespace TradeFlow.Application.Common.Security;

/// <summary>
/// Transport-agnostic abstraction representing the authenticated caller and their institutional tenant scope (ADR-0013).
/// Keeps <c>TradeFlow.Application</c> completely decoupled from <c>HttpContext</c> and <c>ClaimsPrincipal</c>.
/// </summary>
public interface ICurrentUserContext
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }
    Guid? CustomerId { get; }
    IReadOnlySet<string> Roles { get; }
    bool IsInRole(string role);
}
