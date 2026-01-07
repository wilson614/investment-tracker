import type { PortfolioSummary, XirrResult } from '../../types';

interface PerformanceMetricsProps {
  summary: PortfolioSummary;
  xirrResult?: XirrResult | null;
  homeCurrency?: string;
  isLoading?: boolean;
}

export function PerformanceMetrics({
  summary,
  xirrResult,
  homeCurrency = 'TWD',
  isLoading = false,
}: PerformanceMetricsProps) {
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

  const pnlColor = (summary.totalUnrealizedPnlHome ?? 0) >= 0
    ? 'number-positive'
    : 'number-negative';

  const xirrColor = (xirrResult?.xirrPercentage ?? 0) >= 0
    ? 'number-positive'
    : 'number-negative';

  if (isLoading) {
    return (
      <div className="card-dark p-6 animate-pulse">
        <div className="h-6 bg-[var(--bg-tertiary)] rounded w-1/3 mb-4"></div>
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
          {[1, 2, 3, 4].map((i) => (
            <div key={i} className="space-y-2">
              <div className="h-4 bg-[var(--bg-tertiary)] rounded w-2/3"></div>
              <div className="h-6 bg-[var(--bg-tertiary)] rounded w-full"></div>
            </div>
          ))}
        </div>
      </div>
    );
  }

  return (
    <div className="card-dark p-6">
      <h2 className="text-xl font-bold text-[var(--text-primary)] mb-6">投資組合績效</h2>

      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <div className="metric-card metric-card-cream">
          <p className="text-sm text-[var(--text-muted)] mb-1">總成本</p>
          <p className="text-xl font-bold text-[var(--accent-cream)] number-display">
            {formatNumber(summary.totalCostHome)}
          </p>
          <p className="text-sm text-[var(--text-muted)]">{homeCurrency}</p>
        </div>

        <div className="metric-card metric-card-sand">
          <p className="text-sm text-[var(--text-muted)] mb-1">目前市值</p>
          <p className="text-xl font-bold text-[var(--accent-sand)] number-display">
            {summary.totalValueHome != null
              ? formatNumber(summary.totalValueHome)
              : '-'}
          </p>
          <p className="text-sm text-[var(--text-muted)]">{homeCurrency}</p>
        </div>

        <div className="metric-card metric-card-peach">
          <p className="text-sm text-[var(--text-muted)] mb-1">未實現損益</p>
          <p className={`text-xl font-bold number-display ${pnlColor}`}>
            {summary.totalUnrealizedPnlHome != null
              ? formatNumber(summary.totalUnrealizedPnlHome)
              : '-'}
          </p>
          {summary.totalUnrealizedPnlPercentage != null && (
            <p className={`text-sm ${pnlColor}`}>
              {formatPercent(summary.totalUnrealizedPnlPercentage)}
            </p>
          )}
        </div>

        <div className="metric-card metric-card-blush">
          <p className="text-sm text-[var(--text-muted)] mb-1">年化報酬率 (XIRR)</p>
          {xirrResult?.xirrPercentage != null ? (
            <p className={`text-xl font-bold number-display ${xirrColor}`}>
              {formatPercent(xirrResult.xirrPercentage)}
            </p>
          ) : (
            <p className="text-xl font-bold text-[var(--text-muted)]">-</p>
          )}
          {xirrResult && (
            <p className="text-sm text-[var(--text-muted)]">
              基於 {xirrResult.cashFlowCount} 筆交易計算
            </p>
          )}
        </div>
      </div>

      <div className="mt-4 text-sm text-[var(--text-muted)]">
        <p>持倉數量: {summary.positions.length}</p>
      </div>
    </div>
  );
}
