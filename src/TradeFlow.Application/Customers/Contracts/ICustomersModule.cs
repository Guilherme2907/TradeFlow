namespace TradeFlow.Application.Customers.Contracts;

public sealed record CustomerContractKnowledgeSnapshot(
    Guid CustomerId,
    string CompanyName,
    string TaxId,
    string Tier,
    decimal CreditLimit,
    string Currency,
    int PaymentTermsDays,
    string ContractNumber,
    string ContractTitle,
    string TermsText,
    decimal DiscountPercent,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Explicit in-process public contract exposed by the Customers bounded context (ADR-0007 / ADR-0021).
/// Allows authorized internal services (such as the AI RAG ingestion pipeline) to read customer contract snapshots
/// without coupling to Customers domain entities or persistence tables.
/// </summary>
public interface ICustomersModule
{
    Task<IReadOnlyList<CustomerContractKnowledgeSnapshot>> ListActiveContractSnapshotsAsync(
        CancellationToken cancellationToken = default);
}
