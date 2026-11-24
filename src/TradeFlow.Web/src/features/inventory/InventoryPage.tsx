import { useState, type FormEvent } from 'react';
import { Package, PlusCircle, TrendingUp } from 'lucide-react';
import { ErrorBanner } from '../../shared/components/ErrorBanner';
import { formatShortId } from '../../shared/utils/formatters';
import type { ProductDto } from '../catalog/catalogApi';
import {
  useCreateInventoryItemMutation,
  useInventoryQuery,
  useRestockInventoryMutation
} from './useInventoryQuery';

interface InventoryPageProps {
  isAdmin: boolean;
  catalogProducts: ProductDto[];
}

export function InventoryPage({ isAdmin, catalogProducts }: InventoryPageProps) {
  const inventoryQuery = useInventoryQuery();
  const createMutation = useCreateInventoryItemMutation();
  const restockMutation = useRestockInventoryMutation();

  const [restockAmounts, setRestockAmounts] = useState<Record<string, string>>({});
  const [selectedCatalogProductId, setSelectedCatalogProductId] = useState('');
  const [initialQuantity, setInitialQuantity] = useState('50');

  const items = inventoryQuery.data ?? [];
  const trackedProductIds = new Set(items.map((i) => i.productId));
  const untrackedProducts = catalogProducts.filter((p) => !trackedProductIds.has(p.productId));

  const selectedCatalogProduct =
    untrackedProducts.find((p) => p.productId === selectedCatalogProductId) ?? untrackedProducts[0];

  const handleRegisterStock = (e: FormEvent) => {
    e.preventDefault();
    if (!selectedCatalogProduct) return;

    createMutation.mutate({
      productId: selectedCatalogProduct.productId,
      sku: selectedCatalogProduct.sku,
      initialQuantity: Number(initialQuantity)
    });
  };

  const handleRestock = (productId: string) => {
    const qty = Number(restockAmounts[productId] || '25');
    if (qty <= 0) return;
    restockMutation.mutate({ productId, quantity: qty });
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      {inventoryQuery.error ? <ErrorBanner error={inventoryQuery.error} /> : null}
      {restockMutation.error ? (
        <ErrorBanner error={restockMutation.error} onDismiss={() => restockMutation.reset()} />
      ) : null}

      <div className={isAdmin ? 'tf-workspace-grid' : undefined}>
        <section className="tf-card" aria-label="Warehouse Inventory Table">
          <div className="tf-section-header" style={{ marginBottom: 14 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <Package size={17} color="var(--tf-accent-cyan)" />
              <h2 className="tf-section-title">Warehouse Stock Ledger</h2>
            </div>
            <span className="tf-mono" style={{ fontSize: 11, color: 'var(--tf-text-muted)' }}>
              {items.length} tracked SKUs (Pessimistic Lock FOR UPDATE)
            </span>
          </div>

          <div className="tf-table-wrapper">
            <table className="tf-table">
              <thead>
                <tr>
                  <th>SKU</th>
                  <th>Product ID</th>
                  <th>On Hand</th>
                  <th>Allocated</th>
                  <th>Available</th>
                  {isAdmin ? <th>Admin Restock</th> : null}
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr key={item.productId}>
                    <td className="tf-mono" style={{ fontWeight: 700 }}>
                      {item.sku}
                    </td>
                    <td className="tf-mono" style={{ color: 'var(--tf-text-muted)', fontSize: 12 }}>
                      {formatShortId(item.productId)}
                    </td>
                    <td className="tf-mono">{item.quantityOnHand}</td>
                    <td className="tf-mono" style={{ color: 'var(--tf-warning)' }}>
                      {item.allocatedQuantity}
                    </td>
                    <td className="tf-mono" style={{ fontWeight: 700, color: 'var(--tf-accent-primary)' }}>
                      {item.availableQuantity}
                    </td>
                    {isAdmin ? (
                      <td>
                        <div style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                          <input
                            type="number"
                            min="1"
                            className="tf-input"
                            style={{ width: 78, height: 30, fontSize: 12 }}
                            value={restockAmounts[item.productId] ?? '25'}
                            onChange={(e) =>
                              setRestockAmounts((prev) => ({ ...prev, [item.productId]: e.target.value }))
                            }
                          />
                          <button
                            type="button"
                            className="tf-btn-secondary"
                            style={{ height: 30, fontSize: 12, padding: '0 10px' }}
                            disabled={restockMutation.isPending}
                            onClick={() => handleRestock(item.productId)}
                          >
                            <TrendingUp size={13} />
                            Restock
                          </button>
                        </div>
                      </td>
                    ) : null}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>

        {isAdmin ? (
          <aside>
            <section className="tf-card" aria-label="Register Inventory Tracking">
              <div className="tf-section-header" style={{ marginBottom: 12 }}>
                <h3 className="tf-section-title">Track Catalog SKU (Admin)</h3>
                <span className="tf-mono" style={{ fontSize: 11, color: 'var(--tf-text-muted)' }}>
                  POST /api/inventory
                </span>
              </div>

              {createMutation.error ? (
                <div style={{ marginBottom: 12 }}>
                  <ErrorBanner error={createMutation.error} onDismiss={() => createMutation.reset()} />
                </div>
              ) : null}

              {untrackedProducts.length === 0 ? (
                <p style={{ margin: 0, fontSize: 13, color: 'var(--tf-text-secondary)' }}>
                  Every product in the Catalog is already tracked in Inventory. Add a new commodity in the
                  Catalog tab to register its initial stock here.
                </p>
              ) : (
                <form onSubmit={handleRegisterStock} style={{ display: 'grid', gap: 12 }}>
                  <div>
                    <label className="tf-label">Catalog Commodity</label>
                    <select
                      className="tf-select"
                      value={selectedCatalogProduct?.productId ?? ''}
                      onChange={(e) => setSelectedCatalogProductId(e.target.value)}
                    >
                      {untrackedProducts.map((p) => (
                        <option key={p.productId} value={p.productId}>
                          {p.sku} — {p.name}
                        </option>
                      ))}
                    </select>
                  </div>
                  <div>
                    <label className="tf-label">Initial Stock Quantity</label>
                    <input
                      type="number"
                      min="0"
                      required
                      className="tf-input"
                      value={initialQuantity}
                      onChange={(e) => setInitialQuantity(e.target.value)}
                    />
                  </div>
                  <button type="submit" className="tf-btn-primary" disabled={createMutation.isPending}>
                    <PlusCircle size={15} />
                    {createMutation.isPending ? 'Registering...' : 'Register Stock'}
                  </button>
                </form>
              )}
            </section>
          </aside>
        ) : null}
      </div>
    </div>
  );
}
