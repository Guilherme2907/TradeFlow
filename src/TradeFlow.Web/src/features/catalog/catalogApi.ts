import { apiFetch } from '../../shared/api/httpClient';

export interface ProductDto {
  productId: string;
  sku: string;
  name: string;
  category: string;
  technicalDescription: string;
  unitPrice: number;
  currency: string;
  unitOfMeasure: string;
  minimumOrderQuantity: number;
  isActive: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

export interface PagedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface CatalogSearchParams {
  search?: string;
  category?: string;
  currency?: string;
  activeOnly?: boolean;
  page?: number;
  pageSize?: number;
}

export interface CreateProductPayload {
  sku: string;
  name: string;
  category: string;
  technicalDescription: string;
  unitPrice: number;
  currency: string;
  unitOfMeasure?: string;
  minimumOrderQuantity?: number;
}

export interface UpdateProductPayload {
  name: string;
  category: string;
  technicalDescription: string;
  unitPrice: number;
  currency: string;
  unitOfMeasure: string;
  minimumOrderQuantity: number;
  isActive: boolean;
}

export async function fetchCatalogProducts(
  params: CatalogSearchParams = {}
): Promise<PagedResponse<ProductDto>> {
  const qs = new URLSearchParams();
  if (params.search?.trim()) qs.set('search', params.search.trim());
  if (params.category?.trim()) qs.set('category', params.category.trim());
  if (params.currency?.trim()) qs.set('currency', params.currency.trim());
  if (params.activeOnly !== undefined) qs.set('activeOnly', String(params.activeOnly));
  if (params.page) qs.set('page', String(params.page));
  if (params.pageSize) qs.set('pageSize', String(params.pageSize));

  const query = qs.toString();
  return apiFetch<PagedResponse<ProductDto>>(`/api/catalog/products${query ? `?${query}` : ''}`);
}

export async function createCatalogProduct(payload: CreateProductPayload): Promise<string> {
  return apiFetch<string>('/api/catalog/products', {
    method: 'POST',
    body: JSON.stringify(payload)
  });
}

export async function updateCatalogProduct(
  productId: string,
  payload: UpdateProductPayload
): Promise<void> {
  await apiFetch<void>(`/api/catalog/products/${productId}`, {
    method: 'PUT',
    body: JSON.stringify(payload)
  });
}
