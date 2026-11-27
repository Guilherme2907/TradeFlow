import { useState, type FormEvent } from 'react';
import {
  BookMarked,
  CheckCircle2,
  Cpu,
  Database,
  FileLock2,
  Globe,
  RefreshCw,
  Search,
  ShieldAlert,
  Sparkles
} from 'lucide-react';
import { useMutation } from '@tanstack/react-query';
import { ErrorBanner } from '../../shared/components/ErrorBanner';
import {
  askKnowledgeQuestion,
  reindexKnowledgeBase,
  type KnowledgeAnswerDto,
  type KnowledgeReindexDto
} from './aiApi';

interface KnowledgeCopilotPageProps {
  isAdmin: boolean;
}

const PRESET_MY_CONTRACT =
  'Qual é o nosso desconto de rebate contratual, prazo de pagamento (Net days), limite de crédito e cláusulas de SLA acordadas?';

const PRESET_TECHNICAL_SPEC =
  'Qual liga metálica do catálogo é recomendada para ambientes marítimos offshore e reatores químicos, e qual norma ASTM ela atende?';

const PRESET_CROSS_TENANT_DRILL =
  'Qual é o percentual de desconto confidencial e o limite de crédito acordados no contrato da Beta Metals Corp?';

export function KnowledgeCopilotPage({ isAdmin }: KnowledgeCopilotPageProps) {
  const [question, setQuestion] = useState(PRESET_MY_CONTRACT);
  const [result, setResult] = useState<KnowledgeAnswerDto | null>(null);
  const [reindexStats, setReindexStats] = useState<KnowledgeReindexDto | null>(null);

  const askMutation = useMutation({
    mutationFn: (q: string) => askKnowledgeQuestion(q, 5),
    onSuccess: (data) => setResult(data)
  });

  const reindexMutation = useMutation({
    mutationFn: () => reindexKnowledgeBase(),
    onSuccess: (stats) => setReindexStats(stats)
  });

  const handleSubmit = (e: FormEvent) => {
    e.preventDefault();
    if (!question.trim()) {
      return;
    }
    askMutation.mutate(question.trim());
  };

  return (
    <div style={{ display: 'grid', gap: 20 }}>
      <section className="tf-card" aria-label="Multi-Tenant RAG Knowledge Copilot">
        <div className="tf-section-header" style={{ flexWrap: 'wrap', gap: 12 }}>
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <Sparkles size={18} color="var(--tf-accent-cyan)" />
              <h2 className="tf-section-title">TradeFlow Knowledge &amp; Contract Copilot (Hybrid RAG)</h2>
            </div>
            <p style={{ margin: '4px 0 0', fontSize: 13, color: 'var(--tf-text-secondary)' }}>
              Combines PostgreSQL <code>pgvector</code> semantic search + <code>tsvector</code> lexical search via Reciprocal Rank Fusion (RRF), enforcing strict SQL <code>customer_id</code> pre-filtering before Top-K ranking.
            </p>
          </div>

          {isAdmin ? (
            <button
              type="button"
              className="tf-btn-secondary"
              disabled={reindexMutation.isPending}
              onClick={() => reindexMutation.mutate()}
            >
              <RefreshCw size={14} />
              {reindexMutation.isPending ? 'Embedding & Indexing...' : 'Re-index pgvector Store'}
            </button>
          ) : null}
        </div>

        {reindexMutation.error ? (
          <div style={{ marginBottom: 14 }}>
            <ErrorBanner error={reindexMutation.error} onDismiss={() => reindexMutation.reset()} />
          </div>
        ) : null}

        {reindexStats ? (
          <div
            className="tf-mono"
            style={{
              marginBottom: 14,
              padding: '8px 12px',
              borderRadius: 'var(--tf-radius-sm)',
              background: 'rgba(16, 185, 129, 0.12)',
              border: '1px solid rgba(16, 185, 129, 0.35)',
              fontSize: 12,
              color: '#6ee7b7'
            }}
          >
            Indexed {reindexStats.totalChunksStored} chunks ({reindexStats.catalogProductsIndexed} products +{' '}
            {reindexStats.customerContractsIndexed} contracts) under <strong>{reindexStats.embeddingModel}</strong> in{' '}
            {reindexStats.elapsedMs}ms.
          </div>
        ) : null}

        <form onSubmit={handleSubmit} style={{ display: 'grid', gap: 12 }}>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            <button
              type="button"
              className="tf-btn-secondary"
              style={{ height: 28, fontSize: 12 }}
              onClick={() => setQuestion(PRESET_MY_CONTRACT)}
            >
              <FileLock2 size={13} />
              My Confidential Contract Terms
            </button>
            <button
              type="button"
              className="tf-btn-secondary"
              style={{ height: 28, fontSize: 12 }}
              onClick={() => setQuestion(PRESET_TECHNICAL_SPEC)}
            >
              <Globe size={13} />
              Cross-Language ASTM Alloy Spec
            </button>
            <button
              type="button"
              className="tf-btn-secondary"
              style={{ height: 28, fontSize: 12 }}
              onClick={() => setQuestion(PRESET_CROSS_TENANT_DRILL)}
            >
              <ShieldAlert size={13} />
              Test Cross-Tenant Leak Prevention (Beta Corp)
            </button>
          </div>

          <div style={{ display: 'flex', gap: 10 }}>
            <input
              type="text"
              aria-label="Knowledge base question"
              className="tf-input"
              style={{ flex: 1 }}
              value={question}
              onChange={(e) => setQuestion(e.target.value)}
              placeholder="Ask about your negotiated contract rebates, credit limits, or ASTM commodity specifications..."
              required
            />
            <button type="submit" className="tf-btn-primary" disabled={askMutation.isPending}>
              <Search size={15} />
              {askMutation.isPending ? 'Searching pgvector + Generating...' : 'Ask Copilot'}
            </button>
          </div>
        </form>

        {askMutation.error ? (
          <div style={{ marginTop: 14 }}>
            <ErrorBanner error={askMutation.error} onDismiss={() => askMutation.reset()} />
          </div>
        ) : null}
      </section>

      {result ? (
        <div className="tf-workspace-grid">
          <section className="tf-card" aria-label="Grounded Answer">
            <div className="tf-section-header" style={{ flexWrap: 'wrap', gap: 8 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <CheckCircle2
                  size={17}
                  color={result.hasSufficientEvidence ? 'var(--tf-accent-primary)' : 'var(--tf-warning)'}
                />
                <h3 className="tf-section-title">
                  {result.hasSufficientEvidence ? 'Grounded Evidence Answer' : 'Insufficient Authorized Evidence'}
                </h3>
              </div>

              <div className="tf-mono" style={{ fontSize: 11, color: 'var(--tf-accent-cyan)', display: 'flex', gap: 10 }}>
                <span>
                  <Database size={12} style={{ verticalAlign: 'middle', marginRight: 4 }} />
                  {result.embeddingModel}
                  {result.autoIndexedOnDemand ? ' (auto-indexed)' : ''}
                </span>
                <span>
                  <Cpu size={12} style={{ verticalAlign: 'middle', marginRight: 4 }} />
                  {result.telemetry.provider} · {result.telemetry.modelId} ({result.telemetry.inputTokens} in /{' '}
                  {result.telemetry.outputTokens} out · {result.telemetry.latencyMs}ms)
                </span>
              </div>
            </div>

            <div
              style={{
                padding: '14px 16px',
                borderRadius: 'var(--tf-radius-sm)',
                background: 'rgba(6, 9, 19, 0.65)',
                border: '1px solid var(--tf-border-subtle)',
                fontSize: 14,
                lineHeight: 1.65,
                whiteSpace: 'pre-wrap'
              }}
            >
              {result.answer}
            </div>
          </section>

          <section className="tf-card" aria-label="Retrieved Hybrid Chunks">
            <div className="tf-section-header">
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <BookMarked size={16} color="var(--tf-accent-cyan)" />
                <h3 className="tf-section-title">Retrieved Chunks ({result.citations.length})</h3>
              </div>
            </div>

            {result.citations.length === 0 ? (
              <p style={{ fontSize: 13, color: 'var(--tf-text-secondary)', margin: 0 }}>
                Zero chunks matched your tenant scope.
              </p>
            ) : (
              <ul style={{ listStyle: 'none', margin: 0, padding: 0, display: 'grid', gap: 10 }}>
                {result.citations.map((cit, i) => (
                  <li
                    key={`${cit.sourceId}-${i}`}
                    style={{
                      padding: '10px 12px',
                      borderRadius: 'var(--tf-radius-sm)',
                      background: cit.citedByModel ? 'rgba(16, 185, 129, 0.08)' : 'rgba(6, 9, 19, 0.55)',
                      border: cit.citedByModel
                        ? '1px solid rgba(16, 185, 129, 0.4)'
                        : '1px solid var(--tf-border-subtle)',
                      display: 'grid',
                      gap: 6
                    }}
                  >
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                      <span className="tf-mono" style={{ fontSize: 12, fontWeight: 700 }}>
                        {cit.sourceId}
                      </span>
                      <div style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                        {cit.citedByModel ? (
                          <span className="tf-role-tag" style={{ background: 'rgba(16, 185, 129, 0.2)', color: '#6ee7b7' }}>
                            CITED
                          </span>
                        ) : null}
                        <span className="tf-role-tag">
                          {cit.isTenantConfidential ? 'TENANT CONTRACT' : 'PUBLIC CATALOG'}
                        </span>
                      </div>
                    </div>

                    <div style={{ fontSize: 12, fontWeight: 600 }}>{cit.title}</div>
                    <div style={{ fontSize: 12, color: 'var(--tf-text-secondary)', lineHeight: 1.45 }}>
                      {cit.snippet}
                    </div>

                    <div className="tf-mono" style={{ fontSize: 11, color: 'var(--tf-text-muted)', display: 'flex', gap: 10, flexWrap: 'wrap' }}>
                      <span>Cosine: {cit.cosineSimilarity.toFixed(3)}</span>
                      <span>RRF: {cit.rrfScore.toFixed(4)}</span>
                      <span>
                        Match: {[cit.matchedVector ? 'Vector' : null, cit.matchedLexical ? 'Lexical' : null].filter(Boolean).join(' + ')}
                      </span>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </div>
      ) : null}
    </div>
  );
}
