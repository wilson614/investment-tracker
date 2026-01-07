import type { StockPosition } from '../../types';

interface PositionCardProps {
  position: StockPosition;
  baseCurrency?: string;
  homeCurrency?: string;
}

export function PositionCard({ position, baseCurrency = 'USD', homeCurrency = 'TWD' }: PositionCardProps) {
  const formatNumber = (value: number | null | undefined, decimals = 2) => {
    if (value == null) return '-';
    return value.toLocaleString('zh-TW', {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    });
  };

  const formatPercent = (value: number | null | undefined) => {
    if (value == null) return '-';
    const sign = value >= 0 ? '+' : '';
    return `${sign}${formatNumber(value, 2)}%`;
  };

  const pnlColor = (position.unrealizedPnlHome ?? 0) >= 0
    ? 'number-positive'
    : 'number-negative';

  return (
    <div className="card-dark p-5 hover:border-[var(--border-hover)] transition-all">
      <div className="flex justify-between items-start mb-4">
        <h3 className="text-lg font-bold text-[var(--accent-cream)]">{position.ticker}</h3>
        <span className="text-base text-[var(--text-muted)] number-display">
          {formatNumber(position.totalShares, 4)} 股
        </span>
      </div>

      <div className="space-y-3 text-base">
        <div className="flex justify-between">
          <span className="text-[var(--text-muted)]">平均成本:</span>
          <span className="font-medium text-[var(--text-primary)] number-display">
            {formatNumber(position.averageCostPerShareSource)} {baseCurrency}
          </span>
        </div>

        <div className="flex justify-between">
          <span className="text-[var(--text-muted)]">總成本:</span>
          <span className="font-medium text-[var(--text-primary)] number-display">
            {formatNumber(position.totalCostHome)} {homeCurrency}
          </span>
        </div>

        {position.currentValueHome !== undefined && (
          <>
            <hr className="border-[var(--border-color)] my-3" />

            <div className="flex justify-between">
              <span className="text-[var(--text-muted)]">現值:</span>
              <span className="font-medium text-[var(--text-primary)] number-display">
                {formatNumber(position.currentValueHome)} {homeCurrency}
              </span>
            </div>

            <div className="flex justify-between">
              <span className="text-[var(--text-muted)]">未實現損益:</span>
              <span className={`font-medium number-display ${pnlColor}`}>
                {formatNumber(position.unrealizedPnlHome ?? 0)} {homeCurrency}
                {position.unrealizedPnlPercentage !== undefined && (
                  <span className="ml-1 text-sm">
                    ({formatPercent(position.unrealizedPnlPercentage)})
                  </span>
                )}
              </span>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
