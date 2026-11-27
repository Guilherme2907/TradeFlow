import { apiFetch, apiStreamPost } from '../../shared/api/httpClient';

export interface VerifiedDraftLineItemDto {
  productId: string;
  sku: string;
  productName: string;
  category: string;
  unitOfMeasure: string;
  authoritativeUnitPrice: number;
  catalogCurrency: string;
  quantity: number;
  minimumOrderQuantity: number;
  estimatedLineTotal: number;
  matchNote?: string | null;
}

export interface AiTokenUsageSummaryDto {
  provider: string;
  modelId: string;
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
  latencyMs: number;
}

export interface OrderDraftProposalDto {
  recommendedCurrency: string;
  recommendedMinimumOrderValue?: number | null;
  estimatedTotalAmount: number;
  verifiedItems: VerifiedDraftLineItemDto[];
  unmatchedRequests: string[];
  validationWarnings: string[];
  assistantSummary: string;
  telemetry: AiTokenUsageSummaryDto;
}

export interface SseTokenEvent {
  type: 'delta' | 'done' | 'error';
  text: string;
  code?: string | null;
}

export interface KnowledgeCitationDto {
  sourceType: string;
  sourceId: string;
  title: string;
  snippet: string;
  isTenantConfidential: boolean;
  customerId?: string | null;
  cosineSimilarity: number;
  rrfScore: number;
  matchedVector: boolean;
  matchedLexical: boolean;
  citedByModel: boolean;
}

export interface KnowledgeAnswerDto {
  answer: string;
  hasSufficientEvidence: boolean;
  embeddingModel: string;
  autoIndexedOnDemand: boolean;
  citations: KnowledgeCitationDto[];
  telemetry: AiTokenUsageSummaryDto;
}

export interface KnowledgeReindexDto {
  embeddingModel: string;
  catalogProductsIndexed: number;
  customerContractsIndexed: number;
  totalChunksStored: number;
  elapsedMs: number;
}

export async function askKnowledgeQuestion(question: string, topK = 5): Promise<KnowledgeAnswerDto> {
  return apiFetch<KnowledgeAnswerDto>('/api/ai/knowledge/ask', {
    method: 'POST',
    body: JSON.stringify({ question, topK })
  });
}

export async function reindexKnowledgeBase(): Promise<KnowledgeReindexDto> {
  return apiFetch<KnowledgeReindexDto>('/api/ai/knowledge/reindex', {
    method: 'POST',
    body: JSON.stringify({})
  });
}

export async function parseOrderDraftWithAi(prompt: string): Promise<OrderDraftProposalDto> {
  return apiFetch<OrderDraftProposalDto>('/api/ai/orders/parse-draft', {
    method: 'POST',
    body: JSON.stringify({ prompt })
  });
}

export async function streamTradeBriefing(
  question: string,
  onEvent: (evt: SseTokenEvent) => void,
  signal?: AbortSignal
): Promise<void> {
  await apiStreamPost(
    '/api/ai/orders/stream-briefing',
    { question },
    (rawJson) => {
      const parsed = JSON.parse(rawJson) as SseTokenEvent;
      onEvent(parsed);
    },
    signal
  );
}
