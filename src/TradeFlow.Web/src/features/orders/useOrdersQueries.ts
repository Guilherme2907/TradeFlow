import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { INVENTORY_QUERY_KEY } from '../inventory/useInventoryQuery';
import {
  addOrderLine,
  cancelOrder,
  createOrder,
  fetchOrderById,
  fetchOrders,
  submitOrder
} from './ordersApi';
import type { AddOrderLinePayload, CreateOrderPayload } from './types';

export const ORDERS_QUERY_KEY = ['orders'] as const;

export function useOrdersQuery() {
  return useQuery({
    queryKey: ORDERS_QUERY_KEY,
    queryFn: fetchOrders,
    staleTime: 10_000
  });
}

export function useOrderDetailQuery(orderId: string | null) {
  return useQuery({
    queryKey: ['orders', orderId],
    queryFn: () => fetchOrderById(orderId!),
    enabled: Boolean(orderId),
    staleTime: 10_000
  });
}

export function useCreateOrderMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload: CreateOrderPayload) => createOrder(payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ORDERS_QUERY_KEY });
    }
  });
}

export function useAddOrderLineMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ orderId, payload }: { orderId: string; payload: AddOrderLinePayload }) =>
      addOrderLine(orderId, payload),
    onSuccess: (_lineId, variables) => {
      void queryClient.invalidateQueries({ queryKey: ORDERS_QUERY_KEY });
      void queryClient.invalidateQueries({ queryKey: ['orders', variables.orderId] });
    }
  });
}

export function useSubmitOrderMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (orderId: string) => submitOrder(orderId),
    onSuccess: (_data, orderId) => {
      void queryClient.invalidateQueries({ queryKey: ORDERS_QUERY_KEY });
      void queryClient.invalidateQueries({ queryKey: ['orders', orderId] });
      void queryClient.invalidateQueries({ queryKey: INVENTORY_QUERY_KEY });
    }
  });
}

export function useCancelOrderMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ orderId, reason }: { orderId: string; reason: string }) =>
      cancelOrder(orderId, { reason }),
    onSuccess: (_data, variables) => {
      void queryClient.invalidateQueries({ queryKey: ORDERS_QUERY_KEY });
      void queryClient.invalidateQueries({ queryKey: ['orders', variables.orderId] });
      void queryClient.invalidateQueries({ queryKey: INVENTORY_QUERY_KEY });
    }
  });
}
