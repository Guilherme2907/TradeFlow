export interface OrderLineResponse {
  lineId: string;
  productId: string;
  sku: string;
  productName: string;
  unitPrice: number;
  currency: string;
  quantity: number;
  total: number;
}

export interface OrderResponse {
  orderId: string;
  customerId: string;
  status: 'Draft' | 'Submitted' | 'Cancelled' | string;
  currency: string;
  totalAmount: number;
  lines: OrderLineResponse[];
  createdAt: string;
  submittedAt?: string | null;
  cancelledAt?: string | null;
  cancellationReason?: string | null;
}

export interface CreateOrderPayload {
  currency: string;
  minimumOrderValue?: number | null;
}

export interface AddOrderLinePayload {
  productId: string;
  sku: string;
  productName: string;
  unitPrice: number;
  quantity: number;
}

export interface CancelOrderPayload {
  reason: string;
}
