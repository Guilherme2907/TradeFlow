import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiProblemError } from '../../shared/api/problemDetails';
import * as ordersApi from './ordersApi';
import { OrderDetailPanel } from './OrderDetailPanel';
import type { OrderResponse } from './types';

function renderWithQueryClient(ui: React.ReactElement) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false }
    }
  });

  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

const baseDraftOrder: OrderResponse = {
  orderId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
  customerId: '11111111-1111-1111-1111-111111111111',
  status: 'Draft',
  currency: 'USD',
  totalAmount: 2500,
  lines: [
    {
      lineId: '99999999-1111-2222-3333-444444444444',
      productId: '11111111-1111-1111-1111-111111111111',
      sku: 'SKU-STEEL-001',
      productName: 'Cold-Rolled Industrial Steel Coil',
      unitPrice: 1250,
      currency: 'USD',
      quantity: 2,
      total: 2500
    }
  ],
  createdAt: '2026-11-16T12:00:00Z'
};

describe('OrderDetailPanel', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('disables Submit Order button when a Draft order has zero line items', () => {
    const emptyDraft: OrderResponse = { ...baseDraftOrder, totalAmount: 0, lines: [] };

    renderWithQueryClient(
      <OrderDetailPanel order={emptyDraft} canWriteOrders={true} canCancelOrders={true} />
    );

    const submitButton = screen.getByRole('button', { name: /submit order/i });
    expect(submitButton).toBeDisabled();
  });

  it('hides write and cancel action buttons when user lacks RBAC permissions (ComplianceAuditor)', () => {
    renderWithQueryClient(
      <OrderDetailPanel order={baseDraftOrder} canWriteOrders={false} canCancelOrders={false} />
    );

    expect(screen.queryByRole('button', { name: /submit order/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /cancel order/i })).not.toBeInTheDocument();
  });

  it('displays RFC 7807 ProblemDetails alert when order submission hits insufficient stock (409 Conflict)', async () => {
    const user = userEvent.setup();
    vi.spyOn(ordersApi, 'submitOrder').mockRejectedValueOnce(
      new ApiProblemError(409, 'INSUFFICIENT_STOCK', 'Insufficient stock for SKU-STEEL-001.')
    );

    renderWithQueryClient(
      <OrderDetailPanel order={baseDraftOrder} canWriteOrders={true} canCancelOrders={true} />
    );

    await user.click(screen.getByRole('button', { name: /submit order/i }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('[INSUFFICIENT_STOCK] HTTP 409');
    expect(alert).toHaveTextContent('Insufficient stock for SKU-STEEL-001.');
  });
});
