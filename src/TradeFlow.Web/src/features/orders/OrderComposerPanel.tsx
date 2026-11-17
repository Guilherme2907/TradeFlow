import { useState, type FormEvent } from 'react';
import { PlusCircle } from 'lucide-react';
import { ErrorBanner } from '../../shared/components/ErrorBanner';
import type { InventoryItemDto } from '../inventory/inventoryApi';
import { useAddOrderLineMutation, useCreateOrderMutation } from './useOrdersQueries';

interface OrderComposerPanelProps {
  inventoryItems: InventoryItemDto[];
  selectedDraftOrderId: string | null;
  canWriteOrders: boolean;
  onOrderCreated: (orderId: string) => void;
}

const DEFAULT_PRICES: Record<string, { name: string; unitPrice: number }> = {
  'SKU-STEEL-001': { name: 'Cold-Rolled Industrial Steel Coil', unitPrice: 1250 },
  'SKU-COPPER-002': { name: 'Electrolytic Copper Cathode Grade A', unitPrice: 890 }
};

export function OrderComposerPanel({
  inventoryItems,
  selectedDraftOrderId,
  canWriteOrders,
  onOrderCreated
}: OrderComposerPanelProps) {
  const [currency, setCurrency] = useState('USD');
  const [minimumOrderValue, setMinimumOrderValue] = useState('');

  const [selectedProductId, setSelectedProductId] = useState('');
  const [productName, setProductName] = useState('');
  const [unitPrice, setUnitPrice] = useState('1250');
  const [quantity, setQuantity] = useState('2');

  const createMutation = useCreateOrderMutation();
  const addLineMutation = useAddOrderLineMutation();

  const activeInventoryItem =
    inventoryItems.find((x) => x.productId === selectedProductId) ?? inventoryItems[0];

  const handleCreateDraft = (e: FormEvent) => {
    e.preventDefault();
    const minVal = minimumOrderValue.trim() ? Number(minimumOrderValue) : null;
    createMutation.mutate(
      {
        currency: currency.trim().toUpperCase(),
        minimumOrderValue: minVal
      },
      {
        onSuccess: (orderId) => {
          onOrderCreated(orderId);
        }
      }
    );
  };

  const handleAddLine = (e: FormEvent) => {
    e.preventDefault();
    if (!selectedDraftOrderId || !activeInventoryItem) {
      return;
    }

    const fallbackMeta = DEFAULT_PRICES[activeInventoryItem.sku];
    const resolvedName =
      productName.trim() || fallbackMeta?.name || `Commodity ${activeInventoryItem.sku}`;

    addLineMutation.mutate({
      orderId: selectedDraftOrderId,
      payload: {
        productId: activeInventoryItem.productId,
        sku: activeInventoryItem.sku,
        productName: resolvedName,
        unitPrice: Number(unitPrice),
        quantity: Number(quantity)
      }
    });
  };

  if (!canWriteOrders) {
    return (
      <section className="tf-card" aria-label="Read-Only Access Notice">
        <h3 className="tf-section-title" style={{ marginBottom: 8 }}>
          Order Execution Desk
        </h3>
        <p style={{ fontSize: 13, color: 'var(--tf-text-secondary)', margin: 0, lineHeight: 1.5 }}>
          Your current role has read/oversight privileges only. Creating or modifying B2B orders
          requires the <span className="tf-mono">Trader</span> or <span className="tf-mono">Admin</span> role.
        </p>
      </section>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <section className="tf-card" aria-label="Create Draft Order">
        <div className="tf-section-header" style={{ marginBottom: 12 }}>
          <h3 className="tf-section-title">New Purchase Order</h3>
          <span className="tf-mono" style={{ fontSize: 11, color: 'var(--tf-text-muted)' }}>
            POST /api/orders
          </span>
        </div>

        {createMutation.error ? (
          <div style={{ marginBottom: 12 }}>
            <ErrorBanner error={createMutation.error} onDismiss={() => createMutation.reset()} />
          </div>
        ) : null}

        <form onSubmit={handleCreateDraft} style={{ display: 'grid', gap: 12 }}>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
            <div>
              <label htmlFor="order-currency" className="tf-label">
                Currency
              </label>
              <select
                id="order-currency"
                className="tf-select"
                value={currency}
                onChange={(e) => setCurrency(e.target.value)}
              >
                <option value="USD">USD</option>
                <option value="EUR">EUR</option>
                <option value="BRL">BRL</option>
              </select>
            </div>
            <div>
              <label htmlFor="order-min-value" className="tf-label">
                Min Value (Optional)
              </label>
              <input
                id="order-min-value"
                type="number"
                min="0"
                step="0.01"
                className="tf-input"
                placeholder="e.g. 500"
                value={minimumOrderValue}
                onChange={(e) => setMinimumOrderValue(e.target.value)}
              />
            </div>
          </div>

          <button
            type="submit"
            className="tf-btn-primary"
            disabled={createMutation.isPending}
          >
            <PlusCircle size={16} />
            {createMutation.isPending ? 'Creating Draft...' : 'Create Draft Order'}
          </button>
        </form>
      </section>

      {selectedDraftOrderId ? (
        <section className="tf-card" aria-label="Add Order Line Item">
          <div className="tf-section-header" style={{ marginBottom: 12 }}>
            <h3 className="tf-section-title">Add SKU Line to Draft</h3>
            <span className="tf-mono" style={{ fontSize: 11, color: 'var(--tf-accent-cyan)' }}>
              {selectedDraftOrderId.slice(0, 8)}...
            </span>
          </div>

          {addLineMutation.error ? (
            <div style={{ marginBottom: 12 }}>
              <ErrorBanner error={addLineMutation.error} onDismiss={() => addLineMutation.reset()} />
            </div>
          ) : null}

          {inventoryItems.length === 0 ? (
            <p style={{ fontSize: 13, color: 'var(--tf-text-secondary)', margin: 0 }}>
              No inventory SKUs found in database. Seed inventory items before adding lines.
            </p>
          ) : (
            <form onSubmit={handleAddLine} style={{ display: 'grid', gap: 12 }}>
              <div>
                <label htmlFor="line-sku" className="tf-label">
                  Commodity SKU
                </label>
                <select
                  id="line-sku"
                  className="tf-select"
                  value={activeInventoryItem?.productId ?? ''}
                  onChange={(e) => {
                    const nextId = e.target.value;
                    setSelectedProductId(nextId);
                    const found = inventoryItems.find((i) => i.productId === nextId);
                    if (found && DEFAULT_PRICES[found.sku]) {
                      setProductName(DEFAULT_PRICES[found.sku].name);
                      setUnitPrice(String(DEFAULT_PRICES[found.sku].unitPrice));
                    }
                  }}
                >
                  {inventoryItems.map((item) => (
                    <option key={item.productId} value={item.productId}>
                      {item.sku} — Avail: {item.availableQuantity} units
                    </option>
                  ))}
                </select>
              </div>

              <div>
                <label htmlFor="line-product-name" className="tf-label">
                  Contract Snapshot Name
                </label>
                <input
                  id="line-product-name"
                  type="text"
                  className="tf-input"
                  placeholder={
                    (activeInventoryItem && DEFAULT_PRICES[activeInventoryItem.sku]?.name) ||
                    'Industrial Steel Coil'
                  }
                  value={productName}
                  onChange={(e) => setProductName(e.target.value)}
                />
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
                <div>
                  <label htmlFor="line-unit-price" className="tf-label">
                    Unit Price
                  </label>
                  <input
                    id="line-unit-price"
                    type="number"
                    min="0.01"
                    step="0.01"
                    required
                    className="tf-input"
                    value={unitPrice}
                    onChange={(e) => setUnitPrice(e.target.value)}
                  />
                </div>
                <div>
                  <label htmlFor="line-quantity" className="tf-label">
                    Quantity
                  </label>
                  <input
                    id="line-quantity"
                    type="number"
                    min="1"
                    step="1"
                    required
                    className="tf-input"
                    value={quantity}
                    onChange={(e) => setQuantity(e.target.value)}
                  />
                </div>
              </div>

              <button
                type="submit"
                className="tf-btn-secondary"
                style={{ width: '100%', height: 40, fontWeight: 600 }}
                disabled={addLineMutation.isPending}
              >
                {addLineMutation.isPending ? 'Adding Line...' : 'Add Line Snapshot'}
              </button>
            </form>
          )}
        </section>
      ) : null}
    </div>
  );
}
