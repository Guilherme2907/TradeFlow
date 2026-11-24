import { apiFetch } from '../../shared/api/httpClient';
import type {
  AddOrderLinePayload,
  CancelOrderPayload,
  CreateOrderPayload,
  OrderResponse
} from './types';

export interface OrdersFilterParams {
  status?: string;
  currency?: string;
}

export interface CurrencyVolumeSummary {
  currency: string;
  orderCount: number;
  totalAmount: number;
}

export interface OrdersSummaryResponse {
  totalOrders: number;
  draftCount: number;
  submittedCount: number;
  cancelledCount: number;
  submittedVolumeByCurrency: CurrencyVolumeSummary[];
}

export async function fetchOrders(filters: OrdersFilterParams = {}): Promise<OrderResponse[]> {
  const qs = new URLSearchParams();
  if (filters.status) qs.set('status', filters.status);
  if (filters.currency) qs.set('currency', filters.currency);
  const query = qs.toString();
  return apiFetch<OrderResponse[]>(`/api/orders${query ? `?${query}` : ''}`);
}

export async function fetchOrdersSummary(): Promise<OrdersSummaryResponse> {
  return apiFetch<OrdersSummaryResponse>('/api/orders/summary');
}

export async function fetchOrderById(orderId: string): Promise<OrderResponse> {
  return apiFetch<OrderResponse>(`/api/orders/${orderId}`);
}

export async function createOrder(payload: CreateOrderPayload): Promise<string> {
  return apiFetch<string>('/api/orders', {
    method: 'POST',
    body: JSON.stringify(payload)
  });
}

export async function addOrderLine(orderId: string, payload: AddOrderLinePayload): Promise<string> {
  return apiFetch<string>(`/api/orders/${orderId}/lines`, {
    method: 'POST',
    body: JSON.stringify(payload)
  });
}

export async function submitOrder(orderId: string): Promise<void> {
  await apiFetch<void>(`/api/orders/${orderId}/submit`, {
    method: 'POST'
  });
}

export async function cancelOrder(orderId: string, payload: CancelOrderPayload): Promise<void> {
  await apiFetch<void>(`/api/orders/${orderId}/cancel`, {
    method: 'POST',
    body: JSON.stringify(payload)
  });
}
