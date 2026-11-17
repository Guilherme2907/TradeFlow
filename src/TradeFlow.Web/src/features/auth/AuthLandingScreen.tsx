import { BrandMark } from '../../shared/components/BrandMark';

interface AuthLandingScreenProps {
  statusMessage: string;
  isLoading?: boolean;
  isSignedOut?: boolean;
  errorMessage?: string | null;
  onLoginClick: () => void;
}

/**
 * Uses the exact same dark-fintech card shell and CSS design tokens as
 * Keycloak's custom login.ftl theme so transitions are visually transparent (ADR-0018).
 */
export function AuthLandingScreen({
  statusMessage,
  isLoading = false,
  isSignedOut = false,
  errorMessage = null,
  onLoginClick
}: AuthLandingScreenProps) {
  return (
    <main className="tf-auth-shell">
      <section className="tf-auth-card" aria-label="TradeFlow SSO Session Gate">
        <header style={{ marginBottom: 26 }}>
          <div style={{ display: 'flex', justifyContent: 'center', marginBottom: 10 }}>
            <BrandMark />
          </div>
          <p className="tf-brand-subtitle">Institutional B2B Trading &amp; Liquidity Terminal</p>
          <div className="tf-security-pill">
            <span className="tf-security-dot" aria-hidden="true" />
            <span>IN-MEMORY PKCE · ZERO STORAGE TOKEN</span>
          </div>
        </header>

        {errorMessage ? (
          <div className="tf-alert tf-alert-error" role="alert" style={{ marginBottom: 20 }}>
            <strong style={{ display: 'block', marginBottom: 4 }}>SSO Connection Notice</strong>
            <span>{errorMessage}</span>
          </div>
        ) : null}

        {isSignedOut ? (
          <div className="tf-alert tf-alert-info" style={{ marginBottom: 22 }}>
            Your institutional trading session has ended safely and in-memory tokens were cleared.
          </div>
        ) : null}

        {isLoading ? (
          <div style={{ padding: '16px 0' }}>
            <div className="tf-spinner" aria-hidden="true" />
            <p style={{ marginTop: 14, fontSize: 13, color: 'var(--tf-text-secondary)' }}>
              {statusMessage}
            </p>
          </div>
        ) : (
          <button type="button" className="tf-btn-primary" style={{ width: '100%' }} onClick={onLoginClick}>
            Access Trading Desk via SSO
          </button>
        )}
      </section>

      <footer
        style={{
          position: 'relative',
          zIndex: 1,
          marginTop: 20,
          fontSize: 12,
          color: 'var(--tf-text-muted)',
          textAlign: 'center'
        }}
      >
        Secured by Keycloak OAuth2 / OpenID Connect · TradeFlow Platform
      </footer>
    </main>
  );
}
