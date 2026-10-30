namespace TradeFlow.Application.Common.Security;

/// <summary>
/// Canonical institutional roles defined in the Identity Provider (Keycloak) for TradeFlow (ADR-0013).
/// </summary>
public static class TradeFlowRoles
{
    public const string Trader = "Trader";
    public const string RiskManager = "RiskManager";
    public const string ComplianceAuditor = "ComplianceAuditor";
    public const string Admin = "Admin";
}
