interface CompactMetricRowProps {
  label: string;
  value: number;
  description: string;
  color?: 'peach' | 'lavender' | 'mint';
}

const COLOR_MAP = {
  peach: 'var(--accent-peach)',
  lavender: 'var(--accent-blue)',
  mint: 'var(--color-success)',
} as const;

export function CompactMetricRow({ label, value, description, color = 'peach' }: CompactMetricRowProps) {
  const clampedValue = Math.max(0, Math.min(1, value));
  const percentage = Math.round(clampedValue * 1000) / 10;
  const displayPercentage = Number.isInteger(percentage) ? `${percentage}%` : `${percentage.toFixed(1)}%`;

  return (
    <div className="space-y-1.5">
      <div className="flex items-baseline justify-between gap-2">
        <span className="text-xs text-[var(--text-muted)]">{label}</span>
        <span className="text-sm font-mono font-semibold text-[var(--text-primary)]">
          {displayPercentage}
        </span>
      </div>
      <div className="h-2 bg-[var(--bg-tertiary)] rounded-full overflow-hidden">
        <div
          className="h-full rounded-full transition-[width] duration-300"
          style={{
            width: `${clampedValue * 100}%`,
            backgroundColor: COLOR_MAP[color],
          }}
        />
      </div>
      <p className="text-[10px] text-[var(--text-muted)]">{description}</p>
    </div>
  );
}
