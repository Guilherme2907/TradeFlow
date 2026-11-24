import { useState, type FormEvent } from 'react';
import { Building2, FileText, PlusCircle, ShieldCheck } from 'lucide-react';
import { ErrorBanner } from '../../shared/components/ErrorBanner';
import { formatCurrency, formatShortId } from '../../shared/utils/formatters';
import type { CustomerAccountDto } from './customersApi';
import {
  useAddCustomerContractMutation,
  useAllCustomerAccountsQuery,
  useCreateCustomerAccountMutation,
  useMyCustomerAccountQuery
} from './useCustomersQueries';

interface CustomersPageProps {
  hasCustomerId: boolean;
  isPrivilegedRole: boolean;
  isAdmin: boolean;
}

function AccountCard({ account }: { account: CustomerAccountDto }) {
  return (
    <div
      style={{
        padding: 16,
        borderRadius: 'var(--tf-radius-sm)',
        background: 'rgba(6, 9, 19, 0.55)',
        border: '1px solid var(--tf-border-subtle)',
        display: 'grid',
        gap: 12
      }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 12 }}>
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <h3 style={{ margin: 0, fontSize: 16 }}>{account.companyName}</h3>
            <span className="tf-role-tag">{account.tier}</span>
          </div>
          <div className="tf-mono" style={{ fontSize: 11, color: 'var(--tf-text-muted)', marginTop: 4 }}>
            Tenant {formatShortId(account.customerId)} • Tax ID {account.taxId} • Net {account.paymentTermsDays} days
          </div>
        </div>
        <div style={{ textAlign: 'right' }}>
          <div style={{ fontSize: 11, color: 'var(--tf-text-muted)' }}>Credit Limit</div>
          <div className="tf-mono" style={{ fontSize: 16, fontWeight: 700, color: 'var(--tf-accent-primary)' }}>
            {formatCurrency(account.creditLimit, account.currency)}
          </div>
        </div>
      </div>

      {account.contracts.length === 0 ? (
        <div style={{ fontSize: 12, color: 'var(--tf-text-muted)' }}>No commercial contracts attached.</div>
      ) : (
        <div style={{ display: 'grid', gap: 8 }}>
          {account.contracts.map((c) => (
            <div
              key={c.contractId}
              style={{
                padding: 12,
                borderRadius: 'var(--tf-radius-sm)',
                background: 'rgba(15, 23, 42, 0.7)',
                border: '1px solid rgba(56, 189, 248, 0.18)'
              }}
            >
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                  <FileText size={14} color="var(--tf-accent-cyan)" />
                  <span className="tf-mono" style={{ fontSize: 12, fontWeight: 700, color: 'var(--tf-accent-cyan)' }}>
                    {c.contractNumber}
                  </span>
                  <span style={{ fontSize: 13, fontWeight: 600 }}>{c.title}</span>
                </div>
                <span className="tf-mono" style={{ fontSize: 12, color: 'var(--tf-accent-primary)' }}>
                  {c.discountPercent}% rebate
                </span>
              </div>
              <p style={{ margin: '8px 0 0', fontSize: 12.5, color: 'var(--tf-text-secondary)', lineHeight: 1.5 }}>
                {c.termsText}
              </p>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export function CustomersPage({ hasCustomerId, isPrivilegedRole, isAdmin }: CustomersPageProps) {
  const myAccountQuery = useMyCustomerAccountQuery(hasCustomerId);
  const allAccountsQuery = useAllCustomerAccountsQuery(isPrivilegedRole);

  const createAccountMutation = useCreateCustomerAccountMutation();
  const addContractMutation = useAddCustomerContractMutation();

  // Create customer form
  const [companyName, setCompanyName] = useState('');
  const [taxId, setTaxId] = useState('');
  const [tier, setTier] = useState('Gold');
  const [creditLimit, setCreditLimit] = useState('250000');
  const [paymentTermsDays, setPaymentTermsDays] = useState('30');

  // Add contract form
  const [targetCustomerId, setTargetCustomerId] = useState('');
  const [contractNumber, setContractNumber] = useState('');
  const [contractTitle, setContractTitle] = useState('');
  const [termsText, setTermsText] = useState('');
  const [discountPercent, setDiscountPercent] = useState('5');

  const allAccounts = allAccountsQuery.data ?? [];
  const activeTargetId = targetCustomerId || allAccounts[0]?.customerId || '';

  const handleCreateAccount = (e: FormEvent) => {
    e.preventDefault();
    createAccountMutation.mutate(
      {
        companyName,
        taxId,
        tier,
        creditLimit: Number(creditLimit),
        currency: 'USD',
        paymentTermsDays: Number(paymentTermsDays)
      },
      {
        onSuccess: () => {
          setCompanyName('');
          setTaxId('');
        }
      }
    );
  };

  const handleAddContract = (e: FormEvent) => {
    e.preventDefault();
    if (!activeTargetId) return;

    const now = new Date();
    const nextYear = new Date(now.getFullYear() + 1, now.getMonth(), now.getDate());

    addContractMutation.mutate(
      {
        customerId: activeTargetId,
        payload: {
          contractNumber,
          title: contractTitle,
          termsText,
          discountPercent: Number(discountPercent),
          effectiveFrom: now.toISOString(),
          expiresAt: nextYear.toISOString()
        }
      },
      {
        onSuccess: () => {
          setContractNumber('');
          setContractTitle('');
          setTermsText('');
        }
      }
    );
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      {myAccountQuery.error ? <ErrorBanner error={myAccountQuery.error} /> : null}
      {allAccountsQuery.error ? <ErrorBanner error={allAccountsQuery.error} /> : null}

      {hasCustomerId && myAccountQuery.data ? (
        <section className="tf-card" aria-label="My Corporate Account">
          <div className="tf-section-header" style={{ marginBottom: 14 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <ShieldCheck size={17} color="var(--tf-accent-primary)" />
              <h2 className="tf-section-title">My Corporate Account & Confidential Contracts</h2>
            </div>
            <span className="tf-mono" style={{ fontSize: 11, color: 'var(--tf-text-muted)' }}>
              GET /api/customers/me (Tenant Isolated)
            </span>
          </div>
          <AccountCard account={myAccountQuery.data} />
        </section>
      ) : null}

      {isPrivilegedRole ? (
        <div className={isAdmin ? 'tf-workspace-grid' : undefined}>
          <section className="tf-card" aria-label="All Tenant Accounts">
            <div className="tf-section-header" style={{ marginBottom: 14 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <Building2 size={17} color="var(--tf-accent-cyan)" />
                <h2 className="tf-section-title">All B2B Tenant Accounts (Oversight)</h2>
              </div>
              <span className="tf-mono" style={{ fontSize: 11, color: 'var(--tf-text-muted)' }}>
                GET /api/customers ({allAccounts.length} tenants)
              </span>
            </div>

            <div style={{ display: 'grid', gap: 14 }}>
              {allAccounts.map((acc) => (
                <AccountCard key={acc.customerId} account={acc} />
              ))}
            </div>
          </section>

          {isAdmin ? (
            <aside style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
              <section className="tf-card" aria-label="Create B2B Customer">
                <div className="tf-section-header" style={{ marginBottom: 12 }}>
                  <h3 className="tf-section-title">New B2B Account (Admin)</h3>
                </div>
                {createAccountMutation.error ? (
                  <div style={{ marginBottom: 10 }}>
                    <ErrorBanner
                      error={createAccountMutation.error}
                      onDismiss={() => createAccountMutation.reset()}
                    />
                  </div>
                ) : null}
                <form onSubmit={handleCreateAccount} style={{ display: 'grid', gap: 10 }}>
                  <div>
                    <label className="tf-label">Company Name</label>
                    <input
                      className="tf-input"
                      required
                      placeholder="e.g. Gamma Metallurgy S.A."
                      value={companyName}
                      onChange={(e) => setCompanyName(e.target.value)}
                    />
                  </div>
                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
                    <div>
                      <label className="tf-label">Tax ID</label>
                      <input
                        className="tf-input"
                        required
                        placeholder="BR-12-345678"
                        value={taxId}
                        onChange={(e) => setTaxId(e.target.value)}
                      />
                    </div>
                    <div>
                      <label className="tf-label">Tier</label>
                      <select className="tf-select" value={tier} onChange={(e) => setTier(e.target.value)}>
                        <option value="Standard">Standard</option>
                        <option value="Gold">Gold</option>
                        <option value="Enterprise">Enterprise</option>
                      </select>
                    </div>
                  </div>
                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
                    <div>
                      <label className="tf-label">Credit Limit (USD)</label>
                      <input
                        className="tf-input"
                        type="number"
                        min="0"
                        required
                        value={creditLimit}
                        onChange={(e) => setCreditLimit(e.target.value)}
                      />
                    </div>
                    <div>
                      <label className="tf-label">Payment Terms (Days)</label>
                      <input
                        className="tf-input"
                        type="number"
                        min="0"
                        max="180"
                        required
                        value={paymentTermsDays}
                        onChange={(e) => setPaymentTermsDays(e.target.value)}
                      />
                    </div>
                  </div>
                  <button type="submit" className="tf-btn-primary" disabled={createAccountMutation.isPending}>
                    <PlusCircle size={15} />
                    {createAccountMutation.isPending ? 'Creating...' : 'Create Customer'}
                  </button>
                </form>
              </section>

              <section className="tf-card" aria-label="Attach Commercial Contract">
                <div className="tf-section-header" style={{ marginBottom: 12 }}>
                  <h3 className="tf-section-title">Attach Confidential Contract</h3>
                </div>
                {addContractMutation.error ? (
                  <div style={{ marginBottom: 10 }}>
                    <ErrorBanner
                      error={addContractMutation.error}
                      onDismiss={() => addContractMutation.reset()}
                    />
                  </div>
                ) : null}
                <form onSubmit={handleAddContract} style={{ display: 'grid', gap: 10 }}>
                  <div>
                    <label className="tf-label">Target Tenant</label>
                    <select
                      className="tf-select"
                      value={activeTargetId}
                      onChange={(e) => setTargetCustomerId(e.target.value)}
                    >
                      {allAccounts.map((acc) => (
                        <option key={acc.customerId} value={acc.customerId}>
                          {acc.companyName}
                        </option>
                      ))}
                    </select>
                  </div>
                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 90px', gap: 8 }}>
                    <div>
                      <label className="tf-label">Contract Number</label>
                      <input
                        className="tf-input"
                        required
                        placeholder="MSA-2026-02"
                        value={contractNumber}
                        onChange={(e) => setContractNumber(e.target.value)}
                      />
                    </div>
                    <div>
                      <label className="tf-label">Rebate %</label>
                      <input
                        className="tf-input"
                        type="number"
                        step="0.5"
                        min="0"
                        max="50"
                        required
                        value={discountPercent}
                        onChange={(e) => setDiscountPercent(e.target.value)}
                      />
                    </div>
                  </div>
                  <div>
                    <label className="tf-label">Contract Title</label>
                    <input
                      className="tf-input"
                      required
                      placeholder="Lithium & Nickel Supply Addendum"
                      value={contractTitle}
                      onChange={(e) => setContractTitle(e.target.value)}
                    />
                  </div>
                  <div>
                    <label className="tf-label">Confidential Terms & SLA Clauses</label>
                    <textarea
                      className="tf-input"
                      style={{ height: 84, padding: '8px 12px', resize: 'vertical' }}
                      required
                      placeholder="Cancellation penalty, volume rebate threshold, delivery SLA..."
                      value={termsText}
                      onChange={(e) => setTermsText(e.target.value)}
                    />
                  </div>
                  <button type="submit" className="tf-btn-secondary" disabled={addContractMutation.isPending}>
                    <FileText size={15} />
                    {addContractMutation.isPending ? 'Saving...' : 'Add Contract'}
                  </button>
                </form>
              </section>
            </aside>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
