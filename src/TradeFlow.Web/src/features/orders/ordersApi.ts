import { apiFetch } from '../../shared/api/httpClient';
import type {
  AddOrderLinePayload,
  CancelOrderPayload,
  CreateOrderPayload,
  OrderResponse
} from './types';

export async function fetchOrders(): Promise<OrderResponse[]> {
  return apiFetch<OrderResponse[]>('/api/orders');
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
