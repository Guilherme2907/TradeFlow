import { useQuery } from '@tanstack/react-query';
import { fetchInventoryItems } from './inventoryApi';

export const INVENTORY_QUERY_KEY = ['inventory'] as const;

export function useInventoryQuery() {
  return useQuery({
    queryKey: INVENTORY_QUERY_KEY,
    queryFn: fetchInventoryItems,
    staleTime: 15_000
  });
}
