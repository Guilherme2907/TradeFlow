import { useState, type FormEvent } from 'react';
import { Ban, CheckCircle2 } from 'lucide-react';
import { ErrorBanner } from '../../shared/components/ErrorBanner';
import { StatusBadge } from '../../shared/components/StatusBadge';
import { formatCurrency, formatShortId, formatTimestamp } from '../../shared/utils/formatters';
import type { OrderResponse } from './types';
import { useCancelOrderMutation, useSubmitOrderMutation } from './useOrdersQueries';

interface OrderDetailPanelProps {
  order: OrderResponse;
  canWriteOrders: boolean;
  canCancelOrders: boolean;
}

export function OrderDetailPanel({
  order,
  canWriteOrders,
  canCancelOrders
}: OrderDetailPanelProps) {
  const [cancelReason, setCancelReason] = useState('Customer requested cancellation');
  const [showCancelForm, setShowCancelForm] = useState(false);

  const submitMutation = useSubmitOrderMutation();
  const cancelMutation = useCancelOrderMutation();

  const isDraft = order.status === 'Draft';
  const isCancelled = order.status === 'Cancelled';

  const handleSubmitOrder = () => {
    submitMutation.mutate(order.orderId);
  };

  const handleCancelOrder = (e: FormEvent) => {
    e.preventDefault();
    if (!cancelReason.trim()) {
      return;
    }

    cancelMutation.mutate(
      {
        orderId: order.orderId,
        reason: cancelReason.trim()
      },
      {
        onSuccess: () => {
          setShowCancelForm(false);
        }
      }
    );
  };

  return (
    <section className="tf-card" aria-label="Selected Order Details">
      <div className="tf-section-header">
        <div>
          <span className="tf-mono" style={{ fontSize: 11, color: 'var(--tf-text-muted)', display: 'block' }}>
            ORDER {formatShortId(order.orderId)}
          </span>
          <h3 className="tf-section-title" style={{ marginTop: 2 }}>
            {formatCurrency(order.totalAmount, order.currency)}
          </h3>
        </div>
        <StatusBadge status={order.status} />
      </div>

      {submitMutation.error ? (
        <div style={{ marginBottom: 14 }}>
          <ErrorBanner error={submitMutation.error} onDismiss={() => submitMutation.reset()} />
        </div>
      ) : null}

      {cancelMutation.error ? (
        <div style={{ marginBottom: 14 }}>
          <ErrorBanner error={cancelMutation.error} onDismiss={() => cancelMutation.reset()} />
        </div>
      ) : null}

      <div
        style={{
          display: 'grid',
          gridTemplateColumns: '1fr 1fr',
          gap: 10,
          padding: '12px 0',
          borderTop: '1px solid var(--tf-border-subtle)',
          borderBottom: '1px solid var(--tf-border-subtle)',
          marginBottom: 14,
          fontSize: 12
        }}
      >
        <div>
          <span style={{ color: 'var(--tf-text-muted)', display: 'block' }}>Tenant Customer</span>
          <span className="tf-mono" style={{ color: 'var(--tf-text-secondary)' }}>
            {formatShortId(order.customerId)}
          </span>
        </div>
        <div>
          <span style={{ color: 'var(--tf-text-muted)', display: 'block' }}>Created At</span>
          <span style={{ color: 'var(--tf-text-secondary)' }}>{formatTimestamp(order.createdAt)}</span>
        </div>
      </div>

      <h4 style={{ margin: '0 0 10px', fontSize: 12, textTransform: 'uppercase', letterSpacing: '0.06em', color: 'var(--tf-text-muted)' }}>
        Snapshot Lines ({order.lines.length})
      </h4>

      {order.lines.length === 0 ? (
        <p style={{ fontSize: 13, color: 'var(--tf-text-secondary)', margin: '0 0 16px' }}>
          No line items yet. Add at least one SKU before submitting.
        </p>
      ) : (
        <ul style={{ listStyle: 'none', padding: 0, margin: '0 0 16px', display: 'grid', gap: 8 }}>
          {order.lines.map((line) => (
            <li
              key={line.lineId}
              style={{
                padding: '10px 12px',
                borderRadius: 'var(--tf-radius-sm)',
                background: 'rgba(6, 9, 19, 0.55)',
                border: '1px solid var(--tf-border-subtle)',
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                gap: 8
              }}
            >
              <div>
                <div style={{ fontSize: 13, fontWeight: 600 }}>{line.productName}</div>
                <div className="tf-mono" style={{ fontSize: 11, color: 'var(--tf-text-muted)' }}>
                  {line.sku} · {line.quantity} × {formatCurrency(line.unitPrice, line.currency)}
                </div>
              </div>
              <div className="tf-mono" style={{ fontSize: 13, fontWeight: 600 }}>
                {formatCurrency(line.total, line.currency)}
              </div>
            </li>
          ))}
        </ul>
      )}

      {isCancelled && order.cancellationReason ? (
        <div className="tf-alert tf-alert-error" style={{ marginBottom: 14 }}>
          <strong style={{ display: 'block', fontSize: 11, marginBottom: 2 }}>Cancellation Reason</strong>
          <span>{order.cancellationReason}</span>
        </div>
      ) : null}

      <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
        {isDraft && canWriteOrders ? (
          <button
            type="button"
            className="tf-btn-primary"
            style={{ flex: 1 }}
            disabled={order.lines.length === 0 || submitMutation.isPending}
            onClick={handleSubmitOrder}
          >
            <CheckCircle2 size={16} />
            {submitMutation.isPending ? 'Reserving Stock...' : 'Submit Order'}
          </button>
        ) : null}

        {!isCancelled && canCancelOrders ? (
          <button
            type="button"
            className="tf-btn-danger"
            onClick={() => setShowCancelForm((prev) => !prev)}
          >
            <Ban size={15} />
            Cancel Order
          </button>
        ) : null}
      </div>

      {showCancelForm ? (
        <form onSubmit={handleCancelOrder} style={{ marginTop: 14, display: 'grid', gap: 10 }}>
          <div>
            <label htmlFor="cancel-reason" className="tf-label">
              Cancellation Reason
            </label>
            <input
              id="cancel-reason"
              type="text"
              className="tf-input"
              required
              value={cancelReason}
              onChange={(e) => setCancelReason(e.target.value)}
            />
          </div>
          <button
            type="submit"
            className="tf-btn-danger"
            disabled={cancelMutation.isPending}
          >
            {cancelMutation.isPending ? 'Cancelling...' : 'Confirm Cancellation'}
          </button>
        </form>
      ) : null}
    </section>
  );
}
