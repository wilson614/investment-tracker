export type MetricTrend = 'up' | 'down' | 'neutral';

export interface MetricCardProps {
  label: string;
  value: string | number;
  percentage?: number;
  trend?: MetricTrend;
  /** 0-1 之間的比例值，用於顯示進度條 */
  ratio?: number;
  /** 進度條下方的說明文字 */
  description?: string;
}

const TREND_STYLES: Record<MetricTrend, string> = {
  up: 'text-[var(--color-success)] bg-[var(--color-success-soft)] border border-[var(--color-success)]/30',
  down: 'text-[var(--color-danger)] bg-[var(--color-danger-soft)] border border-[var(--color-danger)]/30',
  neutral: 'text-[var(--text-secondary)] bg-[var(--bg-tertiary)] border border-[var(--border-color)]',
};

const TREND_SYMBOLS: Record<MetricTrend, string> = {
  up: '↑',
  down: '↓',
  neutral: '•',
};

function formatMetricValue(value: string | number): string {
  if (typeof value === 'number') {
    return value.toLocaleString('zh-TW');
  }

  return value;
}

function formatPercentageBadge(percentage: number, trend: MetricTrend): string {
  const normalizedValue = Math.abs(percentage);
  const roundedValue = Math.round(normalizedValue * 10) / 10;
  const formattedValue = roundedValue.toLocaleString('zh-TW', {
    minimumFractionDigits: Number.isInteger(roundedValue) ? 0 : 1,
    maximumFractionDigits: 1,
  });

  if (trend === 'up') {
    return `+${formattedValue}%`;
  }

  if (trend === 'down') {
    return `-${formattedValue}%`;
  }

  return `${formattedValue}%`;
}

export function MetricCard({ label, value, percentage, trend = 'neutral', ratio, description }: MetricCardProps) {
  const clampedRatio = typeof ratio === 'number' ? Math.max(0, Math.min(1, ratio)) : undefined;

  return (
    <article className="card-dark p-4 sm:p-5">
      <div className="flex items-start justify-between gap-2">
        <p className="text-xs uppercase tracking-wider text-[var(--text-muted)]">{label}</p>

        {typeof percentage === 'number' ? (
          <span
            className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ${TREND_STYLES[trend]}`}
          >
            <span aria-hidden="true">{TREND_SYMBOLS[trend]}</span>
            {formatPercentageBadge(percentage, trend)}
          </span>
        ) : null}
      </div>

      <p className="mt-3 text-2xl font-bold font-mono text-[var(--text-primary)] number-display">
        {formatMetricValue(value)}
      </p>

      {typeof clampedRatio === 'number' ? (
        <div className="mt-3">
          <div className="h-2 bg-[var(--bg-tertiary)] rounded-full overflow-hidden">
            <div
              className="h-full bg-[var(--accent-peach)] rounded-full transition-all duration-300"
              style={{ width: `${clampedRatio * 100}%` }}
            />
          </div>
          {description ? (
            <p className="mt-1.5 text-xs text-[var(--text-muted)]">{description}</p>
          ) : null}
        </div>
      ) : null}
    </article>
  );
}
