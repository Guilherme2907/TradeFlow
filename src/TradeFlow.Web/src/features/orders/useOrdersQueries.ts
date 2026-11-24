import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { INVENTORY_QUERY_KEY } from '../inventory/useInventoryQuery';
import {
  addOrderLine,
  cancelOrder,
  createOrder,
  fetchOrderById,
  fetchOrders,
  fetchOrdersSummary,
  submitOrder,
  type OrdersFilterParams
} from './ordersApi';
import type { AddOrderLinePayload, CreateOrderPayload } from './types';

export const ORDERS_QUERY_KEY = ['orders'] as const;

export function useOrdersQuery(filters: OrdersFilterParams = {}) {
  return useQuery({
    queryKey: [...ORDERS_QUERY_KEY, 'list', filters],
    queryFn: () => fetchOrders(filters),
    staleTime: 10_000
  });
}

export function useOrdersSummaryQuery() {
  return useQuery({
    queryKey: [...ORDERS_QUERY_KEY, 'summary'],
    queryFn: fetchOrdersSummary,
    staleTime: 10_000
  });
}

export function useOrderDetailQuery(orderId: string | null) {
  return useQuery({
    queryKey: ['orders', 'detail', orderId],
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
    onSuccess: async (_lineId, variables) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ORDERS_QUERY_KEY }),
        queryClient.invalidateQueries({ queryKey: ['orders', 'detail', variables.orderId] })
      ]);
    }
  });
}

export function useSubmitOrderMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (orderId: string) => submitOrder(orderId),
    onSuccess: async (_data, orderId) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ORDERS_QUERY_KEY }),
        queryClient.invalidateQueries({ queryKey: ['orders', 'detail', orderId] }),
        queryClient.invalidateQueries({ queryKey: INVENTORY_QUERY_KEY })
      ]);
    }
  });
}

export function useCancelOrderMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ orderId, reason }: { orderId: string; reason: string }) =>
      cancelOrder(orderId, { reason }),
    onSuccess: async (_data, variables) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ORDERS_QUERY_KEY }),
        queryClient.invalidateQueries({ queryKey: ['orders', 'detail', variables.orderId] }),
        queryClient.invalidateQueries({ queryKey: INVENTORY_QUERY_KEY })
      ]);
    }
  });
}
