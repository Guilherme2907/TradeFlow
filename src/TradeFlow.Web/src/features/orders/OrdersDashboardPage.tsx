import { useMemo, useState } from 'react';
import { LogOut, Package, RefreshCw } from 'lucide-react';
import { BrandMark } from '../../shared/components/BrandMark';
import { ErrorBanner } from '../../shared/components/ErrorBanner';
import { StatusBadge } from '../../shared/components/StatusBadge';
import { formatCurrency, formatShortId, formatTimestamp } from '../../shared/utils/formatters';
import { useAuth } from '../auth/AuthContext';
import { useInventoryQuery } from '../inventory/useInventoryQuery';
import { OrderComposerPanel } from './OrderComposerPanel';
import { OrderDetailPanel } from './OrderDetailPanel';
import { useOrderDetailQuery, useOrdersQuery } from './useOrdersQueries';

export function OrdersDashboardPage() {
  const { session, logout } = useAuth();
  const [selectedOrderId, setSelectedOrderId] = useState<string | null>(null);

  const ordersQuery = useOrdersQuery();
  const inventoryQuery = useInventoryQuery();

  const orders = ordersQuery.data ?? [];
  const inventoryItems = inventoryQuery.data ?? [];

  const activeOrderSummary = useMemo(
    () => orders.find((o) => o.orderId === selectedOrderId) ?? orders[0] ?? null,
    [orders, selectedOrderId]
  );

  // Uses Redis Cache-Aside GET /api/orders/{id} when an order is focused
  const orderDetailQuery = useOrderDetailQuery(activeOrderSummary?.orderId ?? null);
  const selectedOrder = orderDetailQuery.data ?? activeOrderSummary;

  const submittedCount = orders.filter((o) => o.status === 'Submitted').length;
  const draftCount = orders.filter((o) => o.status === 'Draft').length;
  const totalAvailableUnits = inventoryItems.reduce((acc, item) => acc + item.availableQuantity, 0);

  return (
    <div className="tf-app-shell">
      <header className="tf-topbar">
        <div className="tf-topbar-inner">
          <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
            <BrandMark />
            <span className="tf-security-pill" style={{ marginTop: 0 }}>
              <span className="tf-security-dot" aria-hidden="true" />
              <span>{session.isPrivilegedRole ? 'CROSS-TENANT OVERSIGHT' : 'TENANT ISOLATED'}</span>
            </span>
          </div>

          <div className="tf-topbar-user">
            <div className="tf-user-chip">
              <span className="tf-user-name">
                {session.fullName} ({session.username})
              </span>
              <div className="tf-user-meta">
                {session.roles.map((role) => (
                  <span key={role} className="tf-role-tag">
                    {role}
                  </span>
                ))}
                {session.customerId ? (
                  <span>Tenant {formatShortId(session.customerId)}</span>
                ) : null}
              </div>
            </div>

            <button type="button" className="tf-btn-secondary" onClick={() => void logout()}>
              <LogOut size={15} />
              Sign Out
            </button>
          </div>
        </div>
      </header>

      <main className="tf-main-container">
        {ordersQuery.error ? (
          <div style={{ marginBottom: 20 }}>
            <ErrorBanner error={ordersQuery.error} />
          </div>
        ) : null}

        {inventoryQuery.error ? (
          <div style={{ marginBottom: 20 }}>
            <ErrorBanner error={inventoryQuery.error} />
          </div>
        ) : null}

        <section className="tf-kpi-grid" aria-label="Trading Desk Metrics">
          <div className="tf-card">
            <div className="tf-kpi-label">Visible Orders</div>
            <div className="tf-kpi-value">{orders.length}</div>
          </div>
          <div className="tf-card">
            <div className="tf-kpi-label">Submitted Orders</div>
            <div className="tf-kpi-value" style={{ color: 'var(--tf-accent-primary)' }}>
              {submittedCount}
            </div>
          </div>
          <div className="tf-card">
            <div className="tf-kpi-label">Open Drafts</div>
            <div className="tf-kpi-value" style={{ color: 'var(--tf-warning)' }}>
              {draftCount}
            </div>
          </div>
          <div className="tf-card">
            <div className="tf-kpi-label">Available Inventory Units</div>
            <div className="tf-kpi-value" style={{ color: 'var(--tf-accent-cyan)' }}>
              {totalAvailableUnits}
            </div>
          </div>
        </section>

        <section className="tf-card" style={{ marginBottom: 20 }} aria-label="Live Inventory Liquidity">
          <div className="tf-section-header" style={{ marginBottom: 12 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <Package size={17} color="var(--tf-accent-cyan)" />
              <h2 className="tf-section-title">Commodity Inventory Liquidity</h2>
            </div>
            <button
              type="button"
              className="tf-btn-secondary"
              style={{ height: 32, padding: '0 10px', fontSize: 12 }}
              onClick={() => {
                void ordersQuery.refetch();
                void inventoryQuery.refetch();
              }}
            >
              <RefreshCw size={13} />
              Refresh Desk
            </button>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 12 }}>
            {inventoryItems.map((item) => (
              <div
                key={item.productId}
                style={{
                  padding: '12px 14px',
                  borderRadius: 'var(--tf-radius-sm)',
                  background: 'rgba(6, 9, 19, 0.55)',
                  border: '1px solid var(--tf-border-subtle)',
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center'
                }}
              >
                <div>
                  <div className="tf-mono" style={{ fontSize: 13, fontWeight: 600 }}>
                    {item.sku}
                  </div>
                  <div className="tf-mono" style={{ fontSize: 11, color: 'var(--tf-text-muted)' }}>
                    Reserved: {item.allocatedQuantity} / Total: {item.quantityOnHand}
                  </div>
                </div>
                <div className="tf-mono" style={{ fontSize: 16, fontWeight: 700, color: 'var(--tf-accent-primary)' }}>
                  {item.availableQuantity}
                </div>
              </div>
            ))}
          </div>
        </section>

        <div className="tf-workspace-grid">
          <section className="tf-card" aria-label="Order Book">
            <div className="tf-section-header">
              <h2 className="tf-section-title">Purchase Order Book</h2>
              <span className="tf-mono" style={{ fontSize: 11, color: 'var(--tf-text-muted)' }}>
                GET /api/orders
              </span>
            </div>

            {ordersQuery.isLoading ? (
              <div style={{ padding: 36, textAlign: 'center' }}>
                <div className="tf-spinner" />
                <p style={{ fontSize: 13, color: 'var(--tf-text-secondary)', marginTop: 12 }}>
                  Loading order book...
                </p>
              </div>
            ) : orders.length === 0 ? (
              <p style={{ fontSize: 13, color: 'var(--tf-text-secondary)', margin: 0, padding: '18px 0' }}>
                No orders found for your tenant yet. Create your first Draft Order on the right panel.
              </p>
            ) : (
              <div className="tf-table-wrapper">
                <table className="tf-table">
                  <thead>
                    <tr>
                      <th>Order ID</th>
                      {session.isPrivilegedRole ? <th>Tenant</th> : null}
                      <th>Status</th>
                      <th>Lines</th>
                      <th>Total</th>
                      <th>Created</th>
                    </tr>
                  </thead>
                  <tbody>
                    {orders.map((order) => {
                      const isSelected = selectedOrder?.orderId === order.orderId;
                      return (
                        <tr
                          key={order.orderId}
                          className={isSelected ? 'tf-row-selected' : undefined}
                          onClick={() => setSelectedOrderId(order.orderId)}
                        >
                          <td className="tf-mono">{formatShortId(order.orderId)}</td>
                          {session.isPrivilegedRole ? (
                            <td className="tf-mono" style={{ color: 'var(--tf-text-secondary)' }}>
                              {formatShortId(order.customerId)}
                            </td>
                          ) : null}
                          <td>
                            <StatusBadge status={order.status} />
                          </td>
                          <td className="tf-mono">{order.lines.length}</td>
                          <td className="tf-mono" style={{ fontWeight: 600 }}>
                            {formatCurrency(order.totalAmount, order.currency)}
                          </td>
                          <td style={{ color: 'var(--tf-text-secondary)', fontSize: 12 }}>
                            {formatTimestamp(order.createdAt)}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <aside style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
            {selectedOrder ? (
              <OrderDetailPanel
                order={selectedOrder}
                canWriteOrders={session.canWriteOrders}
                canCancelOrders={session.canCancelOrders}
              />
            ) : null}

            <OrderComposerPanel
              inventoryItems={inventoryItems}
              selectedDraftOrderId={
                selectedOrder && selectedOrder.status === 'Draft' ? selectedOrder.orderId : null
              }
              canWriteOrders={session.canWriteOrders}
              onOrderCreated={(newOrderId) => setSelectedOrderId(newOrderId)}
            />
          </aside>
        </div>
      </main>
    </div>
  );
}
