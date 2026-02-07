import { Info } from 'lucide-react';

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
    <div className="space-y-1">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-1">
          <span className="text-xs text-[var(--text-muted)]">{label}</span>
          <div className="relative group">
            <Info className="w-3.5 h-3.5 text-[var(--text-muted)] cursor-help" aria-label={description} />
            <div className="absolute left-0 bottom-full mb-2 hidden group-hover:block z-10 w-max max-w-64">
              <div className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg p-2 shadow-lg text-xs text-[var(--text-secondary)] leading-relaxed whitespace-normal">
                {description}
              </div>
            </div>
          </div>
        </div>
        <span className="text-sm font-mono font-semibold text-[var(--text-primary)]">{displayPercentage}</span>
      </div>
      <div className="h-1.5 bg-[var(--bg-tertiary)] rounded-full overflow-hidden">
        <div
          className="h-full rounded-full transition-[width] duration-300"
          style={{
            width: `${clampedValue * 100}%`,
            backgroundColor: COLOR_MAP[color],
          }}
        />
      </div>
    </div>
  );
}
