import { apiFetch } from '../../shared/api/httpClient';

export interface InventoryItemDto {
  productId: string;
  sku: string;
  quantityOnHand: number;
  allocatedQuantity: number;
  availableQuantity: number;
}

export async function fetchInventoryItems(): Promise<InventoryItemDto[]> {
  return apiFetch<InventoryItemDto[]>('/api/inventory');
}
