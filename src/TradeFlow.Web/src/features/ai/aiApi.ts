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
