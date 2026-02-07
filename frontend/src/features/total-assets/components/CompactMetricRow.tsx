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
    <div className="flex items-center gap-3">
      <div className="flex items-center gap-1 shrink-0">
        <span className="text-sm text-[var(--text-secondary)]">{label}</span>
        <div className="relative group">
          <Info className="w-4 h-4 text-[var(--text-muted)] cursor-help" aria-label={description} />
          <div className="absolute left-0 bottom-full mb-2 hidden group-hover:block z-10 w-max max-w-72">
            <div className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg p-2 shadow-lg text-sm text-[var(--text-secondary)] leading-relaxed whitespace-normal">
              {description}
            </div>
          </div>
        </div>
      </div>

      <div className="flex-1 min-w-0">
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

      <span className="text-sm font-mono font-semibold text-[var(--text-primary)] shrink-0">{displayPercentage}</span>
    </div>
  );
}
