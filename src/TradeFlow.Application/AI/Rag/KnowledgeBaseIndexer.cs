using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using TradeFlow.Application.AI.Gateways;
using TradeFlow.Application.Catalog.Contracts;
using TradeFlow.Application.Customers.Contracts;

namespace TradeFlow.Application.AI.Rag;

public interface IKnowledgeBaseIndexer
{
    string GetActiveEmbeddingModelKey();

    Task<KnowledgeReindexResponse> ReindexAllAsync(CancellationToken cancellationToken = default);
}

public sealed class KnowledgeBaseIndexer : IKnowledgeBaseIndexer
{
    private readonly ICatalogModule _catalogModule;
    private readonly ICustomersModule _customersModule;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly IKnowledgeStore _knowledgeStore;
    private readonly ILogger<KnowledgeBaseIndexer> _logger;

    public KnowledgeBaseIndexer(
        ICatalogModule catalogModule,
        ICustomersModule customersModule,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IKnowledgeStore knowledgeStore,
        ILogger<KnowledgeBaseIndexer> logger)
    {
        _catalogModule = catalogModule;
        _customersModule = customersModule;
        _embeddingGenerator = embeddingGenerator;
        _knowledgeStore = knowledgeStore;
        _logger = logger;
    }

    public string GetActiveEmbeddingModelKey()
    {
        var metadata = _embeddingGenerator.GetService<EmbeddingGeneratorMetadata>();
        var provider = metadata?.ProviderName ?? "AI";
        var model = metadata?.DefaultModelId ?? "default-embedding";
        return $"{provider}:{model}";
    }

    public async Task<KnowledgeReindexResponse> ReindexAllAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var modelKey = GetActiveEmbeddingModelKey();

        var products = await _catalogModule.ListActiveProductsAsync(cancellationToken);
        var contracts = await _customersModule.ListActiveContractSnapshotsAsync(cancellationToken);

        var draftChunks = new List<(string SourceType, string SourceId, int ChunkIndex, Guid? CustomerId, string Title, string Content)>();

        foreach (var p in products)
        {
            var header = $"[Catalog Technical Spec | SourceId: {p.Sku} | Commodity: {p.Name} | Category: {p.Category} | Scope: Public Platform]";
            var body =
                $"SKU {p.Sku} — {p.Name} ({p.Category}). " +
                $"Commercial Unit Price: {p.UnitPrice:F2} {p.Currency} per {p.UnitOfMeasure}. " +
                $"Minimum Order Quantity (MOQ): {p.MinimumOrderQuantity} {p.UnitOfMeasure}. " +
                $"Technical Specification: {p.Description}";

            var textChunks = SemanticTextChunker.ChunkWithContextualHeader(header, body);
            for (var idx = 0; idx < textChunks.Count; idx++)
            {
                draftChunks.Add(("CatalogProduct", p.Sku, idx, null, $"{p.Sku} — {p.Name}", textChunks[idx]));
            }
        }

        foreach (var c in contracts)
        {
            var header =
                $"[Confidential B2B Contract | SourceId: {c.ContractNumber} | Tenant: {c.CompanyName} | Tier: {c.Tier} | Scope: Tenant-Confidential]";
            var body =
                $"Master Commercial Agreement {c.ContractNumber} ({c.ContractTitle}) for corporate account {c.CompanyName} (Tax ID: {c.TaxId}, Tier: {c.Tier}). " +
                $"Approved Credit Limit: {c.CreditLimit:F2} {c.Currency}. Standard Payment Terms: Net {c.PaymentTermsDays} days. " +
                $"Negotiated Rebate / Volume Discount: {c.DiscountPercent:F2}%. " +
                $"Validity Period: {c.EffectiveFrom:yyyy-MM-dd} to {c.ExpiresAt:yyyy-MM-dd}. " +
                $"Contractual Clauses & SLA: {c.TermsText}";

            var textChunks = SemanticTextChunker.ChunkWithContextualHeader(header, body);
            for (var idx = 0; idx < textChunks.Count; idx++)
            {
                draftChunks.Add(("CustomerContract", c.ContractNumber, idx, c.CustomerId, $"{c.ContractNumber} — {c.ContractTitle} ({c.CompanyName})", textChunks[idx]));
            }
        }

        if (draftChunks.Count == 0)
        {
            await _knowledgeStore.ReplaceAllForModelAsync(modelKey, [], cancellationToken);
            sw.Stop();
            return new KnowledgeReindexResponse(modelKey, 0, 0, 0, sw.ElapsedMilliseconds);
        }

        var textsToEmbed = draftChunks.Select(c => c.Content).ToList();
        var generatedEmbeddings = await _embeddingGenerator.GenerateAsync(
            textsToEmbed,
            new EmbeddingGenerationOptions { Dimensions = KnowledgeDimensions.VectorSize },
            cancellationToken);

        var toPersist = new List<KnowledgeChunkToUpsert>(draftChunks.Count);
        for (var i = 0; i < draftChunks.Count; i++)
        {
            var item = draftChunks[i];
            toPersist.Add(new KnowledgeChunkToUpsert(
                Guid.NewGuid(),
                item.SourceType,
                item.SourceId,
                item.ChunkIndex,
                item.CustomerId,
                item.Title,
                item.Content,
                modelKey,
                generatedEmbeddings[i].Vector));
        }

        await _knowledgeStore.ReplaceAllForModelAsync(modelKey, toPersist, cancellationToken);
        sw.Stop();

        _logger.LogInformation(
            "[KnowledgeIndexer] Indexed {ChunkCount} chunks ({ProductCount} products, {ContractCount} contracts) under model '{EmbeddingModel}' in {ElapsedMs}ms.",
            toPersist.Count,
            products.Count,
            contracts.Count,
            modelKey,
            sw.ElapsedMilliseconds);

        return new KnowledgeReindexResponse(
            modelKey,
            products.Count,
            contracts.Count,
            toPersist.Count,
            sw.ElapsedMilliseconds);
    }
}
