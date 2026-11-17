interface StatusBadgeProps {
  status: string;
}

export function StatusBadge({ status }: StatusBadgeProps) {
  const normalized = status.toLowerCase();
  const variantClass =
    normalized === 'submitted'
      ? 'tf-badge-submitted'
      : normalized === 'cancelled'
        ? 'tf-badge-cancelled'
        : 'tf-badge-draft';

  return <span className={`tf-badge ${variantClass}`}>{status}</span>;
}
