import { ApiProblemError } from '../api/problemDetails';

interface ErrorBannerProps {
  error: unknown;
  onDismiss?: () => void;
}

export function ErrorBanner({ error, onDismiss }: ErrorBannerProps) {
  if (!error) {
    return null;
  }

  const isProblem = error instanceof ApiProblemError;
  const title = isProblem ? `[${error.code}] HTTP ${error.status}` : 'Operation Error';
  const detail = error instanceof Error ? error.message : 'An unexpected error occurred.';

  return (
    <div className="tf-alert tf-alert-error" role="alert" style={{ display: 'flex', justifyContent: 'space-between', gap: 12 }}>
      <div>
        <strong className="tf-mono" style={{ display: 'block', fontSize: 12, marginBottom: 4 }}>
          {title}
        </strong>
        <span>{detail}</span>
        {isProblem && error.retryAfterSeconds ? (
          <span className="tf-mono" style={{ display: 'block', fontSize: 11, marginTop: 4 }}>
            Retry-After: {error.retryAfterSeconds}s
          </span>
        ) : null}
      </div>
      {onDismiss ? (
        <button
          type="button"
          className="tf-btn-secondary"
          style={{ height: 28, padding: '0 8px', fontSize: 12 }}
          onClick={onDismiss}
          aria-label="Dismiss error"
        >
          Dismiss
        </button>
      ) : null}
    </div>
  );
}
