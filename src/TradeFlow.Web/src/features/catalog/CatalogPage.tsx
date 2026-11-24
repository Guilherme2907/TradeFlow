import { useState, type FormEvent } from 'react';
import { BookOpen, Edit3, PlusCircle, Search } from 'lucide-react';
import { ErrorBanner } from '../../shared/components/ErrorBanner';
import { formatCurrency } from '../../shared/utils/formatters';
import { type ProductDto } from './catalogApi';
import {
  useCatalogProductsQuery,
  useCreateProductMutation,
  useUpdateProductMutation
} from './useCatalogQueries';

interface CatalogPageProps {
  isAdmin: boolean;
}

const CATEGORIES = [
  'Ferrous Metals',
  'Non-Ferrous Metals',
  'Specialty Alloys',
  'Industrial Polymers',
  'Energy Materials'
];

export function CatalogPage({ isAdmin }: CatalogPageProps) {
  const [search, setSearch] = useState('');
  const [categoryFilter, setCategoryFilter] = useState('');
  const [currencyFilter, setCurrencyFilter] = useState('');
  const [activeOnly, setActiveOnly] = useState(true);

  const [editingProduct, setEditingProduct] = useState<ProductDto | null>(null);

  // Create form state
  const [sku, setSku] = useState('');
  const [name, setName] = useState('');
  const [category, setCategory] = useState('Ferrous Metals');
  const [technicalDescription, setTechnicalDescription] = useState('');
  const [unitPrice, setUnitPrice] = useState('1500');
  const [currency, setCurrency] = useState('USD');
  const [unitOfMeasure, setUnitOfMeasure] = useState('Metric Ton');
  const [moq, setMoq] = useState('1');

  const catalogQuery = useCatalogProductsQuery({
    search,
    category: categoryFilter || undefined,
    currency: currencyFilter || undefined,
    activeOnly,
    page: 1,
    pageSize: 50
  });

  const createMutation = useCreateProductMutation();
  const updateMutation = useUpdateProductMutation();

  const products = catalogQuery.data?.items ?? [];

  const handleCreate = (e: FormEvent) => {
    e.preventDefault();
    createMutation.mutate(
      {
        sku,
        name,
        category,
        technicalDescription,
        unitPrice: Number(unitPrice),
        currency,
        unitOfMeasure,
        minimumOrderQuantity: Number(moq)
      },
      {
        onSuccess: () => {
          setSku('');
          setName('');
          setTechnicalDescription('');
        }
      }
    );
  };

  const handleUpdate = (e: FormEvent) => {
    e.preventDefault();
    if (!editingProduct) return;

    updateMutation.mutate(
      {
        productId: editingProduct.productId,
        payload: {
          name: editingProduct.name,
          category: editingProduct.category,
          technicalDescription: editingProduct.technicalDescription,
          unitPrice: editingProduct.unitPrice,
          currency: editingProduct.currency,
          unitOfMeasure: editingProduct.unitOfMeasure,
          minimumOrderQuantity: editingProduct.minimumOrderQuantity,
          isActive: editingProduct.isActive
        }
      },
      {
        onSuccess: () => setEditingProduct(null)
      }
    );
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      {catalogQuery.error ? <ErrorBanner error={catalogQuery.error} /> : null}

      <section className="tf-card" aria-label="Catalog Search and Filters">
        <div className="tf-section-header" style={{ marginBottom: 14 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <BookOpen size={17} color="var(--tf-accent-primary)" />
            <h2 className="tf-section-title">Industrial Commodity Catalog</h2>
          </div>
          <span className="tf-mono" style={{ fontSize: 11, color: 'var(--tf-text-muted)' }}>
            GET /api/catalog/products ({catalogQuery.data?.totalCount ?? 0} SKUs)
          </span>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr 120px auto', gap: 10, alignItems: 'end' }}>
          <div>
            <label htmlFor="cat-search" className="tf-label">
              Search SKU, Name, or Technical Spec
            </label>
            <div style={{ position: 'relative' }}>
              <Search
                size={14}
                style={{ position: 'absolute', left: 12, top: 13, color: 'var(--tf-text-muted)' }}
              />
              <input
                id="cat-search"
                className="tf-input"
                style={{ paddingLeft: 34 }}
                placeholder="e.g. copper, ASTM A1008, aerospace, lithium..."
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>
          </div>

          <div>
            <label htmlFor="cat-filter" className="tf-label">
              Category
            </label>
            <select
              id="cat-filter"
              className="tf-select"
              value={categoryFilter}
              onChange={(e) => setCategoryFilter(e.target.value)}
            >
              <option value="">All Categories</option>
              {CATEGORIES.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
          </div>

          <div>
            <label htmlFor="cat-curr" className="tf-label">
              Currency
            </label>
            <select
              id="cat-curr"
              className="tf-select"
              value={currencyFilter}
              onChange={(e) => setCurrencyFilter(e.target.value)}
            >
              <option value="">All</option>
              <option value="USD">USD</option>
              <option value="EUR">EUR</option>
              <option value="BRL">BRL</option>
            </select>
          </div>

          <label
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 6,
              height: 40,
              fontSize: 13,
              color: 'var(--tf-text-secondary)',
              cursor: 'pointer'
            }}
          >
            <input
              type="checkbox"
              checked={activeOnly}
              onChange={(e) => setActiveOnly(e.target.checked)}
            />
            Active only
          </label>
        </div>
      </section>

      <div className={isAdmin ? 'tf-workspace-grid' : undefined}>
        <section className="tf-card" aria-label="Catalog Product List">
          {catalogQuery.isLoading ? (
            <div style={{ padding: 32, textAlign: 'center' }}>
              <div className="tf-spinner" />
            </div>
          ) : products.length === 0 ? (
            <p style={{ fontSize: 13, color: 'var(--tf-text-secondary)', margin: 0 }}>
              No catalog products matched the selected filters.
            </p>
          ) : (
            <div style={{ display: 'grid', gap: 12 }}>
              {products.map((p) => (
                <div
                  key={p.productId}
                  style={{
                    padding: 14,
                    borderRadius: 'var(--tf-radius-sm)',
                    background: 'rgba(6, 9, 19, 0.55)',
                    border: '1px solid var(--tf-border-subtle)',
                    display: 'grid',
                    gap: 8
                  }}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 12 }}>
                    <div>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                        <span className="tf-mono" style={{ fontSize: 12, fontWeight: 700, color: 'var(--tf-accent-cyan)' }}>
                          {p.sku}
                        </span>
                        <span className="tf-role-tag">{p.category}</span>
                        {!p.isActive ? (
                          <span className="tf-role-tag" style={{ color: '#fca5a5', borderColor: 'var(--tf-danger-border)' }}>
                            Inactive
                          </span>
                        ) : null}
                      </div>
                      <h3 style={{ margin: '4px 0 0', fontSize: 15 }}>{p.name}</h3>
                    </div>

                    <div style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                      <div className="tf-mono" style={{ fontSize: 16, fontWeight: 700, color: 'var(--tf-accent-primary)' }}>
                        {formatCurrency(p.unitPrice, p.currency)}
                      </div>
                      <div className="tf-mono" style={{ fontSize: 11, color: 'var(--tf-text-muted)' }}>
                        per {p.unitOfMeasure} • MOQ {p.minimumOrderQuantity}
                      </div>
                    </div>
                  </div>

                  <p style={{ margin: 0, fontSize: 12.5, color: 'var(--tf-text-secondary)', lineHeight: 1.5 }}>
                    {p.technicalDescription}
                  </p>

                  {isAdmin ? (
                    <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                      <button
                        type="button"
                        className="tf-btn-secondary"
                        style={{ height: 30, fontSize: 12, padding: '0 10px' }}
                        onClick={() => setEditingProduct({ ...p })}
                      >
                        <Edit3 size={13} />
                        Edit Product
                      </button>
                    </div>
                  ) : null}
                </div>
              ))}
            </div>
          )}
        </section>

        {isAdmin ? (
          <aside style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            {editingProduct ? (
              <section className="tf-card" aria-label="Edit Catalog Product">
                <div className="tf-section-header" style={{ marginBottom: 12 }}>
                  <h3 className="tf-section-title">Edit {editingProduct.sku}</h3>
                  <button
                    type="button"
                    className="tf-btn-secondary"
                    style={{ height: 28, fontSize: 12 }}
                    onClick={() => setEditingProduct(null)}
                  >
                    Cancel
                  </button>
                </div>
                {updateMutation.error ? (
                  <div style={{ marginBottom: 12 }}>
                    <ErrorBanner error={updateMutation.error} onDismiss={() => updateMutation.reset()} />
                  </div>
                ) : null}
                <form onSubmit={handleUpdate} style={{ display: 'grid', gap: 10 }}>
                  <div>
                    <label className="tf-label">Product Name</label>
                    <input
                      className="tf-input"
                      required
                      value={editingProduct.name}
                      onChange={(e) => setEditingProduct({ ...editingProduct, name: e.target.value })}
                    />
                  </div>
                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
                    <div>
                      <label className="tf-label">Unit Price</label>
                      <input
                        className="tf-input"
                        type="number"
                        step="0.01"
                        min="0.01"
                        required
                        value={editingProduct.unitPrice}
                        onChange={(e) =>
                          setEditingProduct({ ...editingProduct, unitPrice: Number(e.target.value) })
                        }
                      />
                    </div>
                    <div>
                      <label className="tf-label">MOQ</label>
                      <input
                        className="tf-input"
                        type="number"
                        min="1"
                        required
                        value={editingProduct.minimumOrderQuantity}
                        onChange={(e) =>
                          setEditingProduct({
                            ...editingProduct,
                            minimumOrderQuantity: Number(e.target.value)
                          })
                        }
                      />
                    </div>
                  </div>
                  <div>
                    <label className="tf-label">Technical Description</label>
                    <textarea
                      className="tf-input"
                      style={{ height: 84, padding: '8px 12px', resize: 'vertical' }}
                      required
                      value={editingProduct.technicalDescription}
                      onChange={(e) =>
                        setEditingProduct({ ...editingProduct, technicalDescription: e.target.value })
                      }
                    />
                  </div>
                  <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13 }}>
                    <input
                      type="checkbox"
                      checked={editingProduct.isActive}
                      onChange={(e) =>
                        setEditingProduct({ ...editingProduct, isActive: e.target.checked })
                      }
                    />
                    Active in Catalog
                  </label>
                  <button type="submit" className="tf-btn-primary" disabled={updateMutation.isPending}>
                    {updateMutation.isPending ? 'Saving...' : 'Save Changes'}
                  </button>
                </form>
              </section>
            ) : null}

            <section className="tf-card" aria-label="Create Catalog Product">
              <div className="tf-section-header" style={{ marginBottom: 12 }}>
                <h3 className="tf-section-title">New Commodity (Admin)</h3>
                <span className="tf-mono" style={{ fontSize: 11, color: 'var(--tf-text-muted)' }}>
                  POST /api/catalog/products
                </span>
              </div>

              {createMutation.error ? (
                <div style={{ marginBottom: 12 }}>
                  <ErrorBanner error={createMutation.error} onDismiss={() => createMutation.reset()} />
                </div>
              ) : null}

              <form onSubmit={handleCreate} style={{ display: 'grid', gap: 10 }}>
                <div>
                  <label className="tf-label">SKU</label>
                  <input
                    className="tf-input"
                    required
                    placeholder="e.g. SKU-COBALT-011"
                    value={sku}
                    onChange={(e) => setSku(e.target.value)}
                  />
                </div>
                <div>
                  <label className="tf-label">Commercial Name</label>
                  <input
                    className="tf-input"
                    required
                    placeholder="e.g. Refined Cobalt Cathode 99.8%"
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                  />
                </div>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
                  <div>
                    <label className="tf-label">Category</label>
                    <select className="tf-select" value={category} onChange={(e) => setCategory(e.target.value)}>
                      {CATEGORIES.map((c) => (
                        <option key={c} value={c}>
                          {c}
                        </option>
                      ))}
                    </select>
                  </div>
                  <div>
                    <label className="tf-label">Unit of Measure</label>
                    <input
                      className="tf-input"
                      required
                      value={unitOfMeasure}
                      onChange={(e) => setUnitOfMeasure(e.target.value)}
                    />
                  </div>
                </div>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 90px 80px', gap: 8 }}>
                  <div>
                    <label className="tf-label">Base Price</label>
                    <input
                      className="tf-input"
                      type="number"
                      step="0.01"
                      min="0.01"
                      required
                      value={unitPrice}
                      onChange={(e) => setUnitPrice(e.target.value)}
                    />
                  </div>
                  <div>
                    <label className="tf-label">Currency</label>
                    <select className="tf-select" value={currency} onChange={(e) => setCurrency(e.target.value)}>
                      <option value="USD">USD</option>
                      <option value="EUR">EUR</option>
                      <option value="BRL">BRL</option>
                    </select>
                  </div>
                  <div>
                    <label className="tf-label">MOQ</label>
                    <input
                      className="tf-input"
                      type="number"
                      min="1"
                      required
                      value={moq}
                      onChange={(e) => setMoq(e.target.value)}
                    />
                  </div>
                </div>
                <div>
                  <label className="tf-label">Technical Specifications (Used by AI / RAG)</label>
                  <textarea
                    className="tf-input"
                    style={{ height: 84, padding: '8px 12px', resize: 'vertical' }}
                    required
                    placeholder="Purity grade, ASTM standard, industrial application..."
                    value={technicalDescription}
                    onChange={(e) => setTechnicalDescription(e.target.value)}
                  />
                </div>
                <button type="submit" className="tf-btn-primary" disabled={createMutation.isPending}>
                  <PlusCircle size={15} />
                  {createMutation.isPending ? 'Creating...' : 'Add to Catalog'}
                </button>
              </form>
            </section>
          </aside>
        ) : null}
      </div>
    </div>
  );
}
