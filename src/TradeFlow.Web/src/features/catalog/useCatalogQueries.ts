import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  createCatalogProduct,
  fetchCatalogProducts,
  updateCatalogProduct,
  type CatalogSearchParams,
  type CreateProductPayload,
  type UpdateProductPayload
} from './catalogApi';

export const CATALOG_QUERY_KEY = ['catalog', 'products'] as const;

export function useCatalogProductsQuery(params: CatalogSearchParams = { pageSize: 50, activeOnly: false }) {
  return useQuery({
    queryKey: [...CATALOG_QUERY_KEY, params],
    queryFn: () => fetchCatalogProducts(params),
    staleTime: 15_000
  });
}

export function useCreateProductMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateProductPayload) => createCatalogProduct(payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: CATALOG_QUERY_KEY });
    }
  });
}

export function useUpdateProductMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ productId, payload }: { productId: string; payload: UpdateProductPayload }) =>
      updateCatalogProduct(productId, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: CATALOG_QUERY_KEY });
    }
  });
}
