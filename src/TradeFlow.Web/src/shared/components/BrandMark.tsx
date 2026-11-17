export function BrandMark() {
  return (
    <div className="tf-brand-logo" style={{ marginBottom: 0 }}>
      <div className="tf-brand-icon" aria-hidden="true">
        <svg
          width="22"
          height="22"
          viewBox="0 0 24 24"
          fill="none"
          stroke="#022c22"
          strokeWidth="2.4"
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <polyline points="22 7 13.5 15.5 8.5 10.5 2 17" />
          <polyline points="16 7 22 7 22 13" />
        </svg>
      </div>
      <span className="tf-brand-name">TradeFlow</span>
    </div>
  );
}
