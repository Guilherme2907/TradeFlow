import { apiFetch } from '../../shared/api/httpClient';

export interface InventoryItemDto {
  productId: string;
  sku: string;
  quantityOnHand: number;
  allocatedQuantity: number;
  availableQuantity: number;
}

export interface CreateInventoryItemPayload {
  productId: string;
  sku: string;
  initialQuantity: number;
}

export async function fetchInventoryItems(): Promise<InventoryItemDto[]> {
  return apiFetch<InventoryItemDto[]>('/api/inventory');
}

export async function createInventoryItem(payload: CreateInventoryItemPayload): Promise<string> {
  return apiFetch<string>('/api/inventory', {
    method: 'POST',
    body: JSON.stringify(payload)
  });
}

export async function restockInventory(
  productId: string,
  quantity: number
): Promise<InventoryItemDto> {
  return apiFetch<InventoryItemDto>(`/api/inventory/${productId}/restock`, {
    method: 'POST',
    body: JSON.stringify({ quantity })
  });
}
