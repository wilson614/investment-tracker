import { Info } from 'lucide-react';
import { isXirrPeriodTooShort } from '../common/XirrWarningBadge';
import type { PortfolioSummary, XirrResult } from '../../types';

interface PerformanceMetricsProps {
  summary: PortfolioSummary;
  xirrResult?: XirrResult | null;
  homeCurrency?: string;
  isLoading?: boolean;
  portfolioId?: string;
}

export function PerformanceMetrics({
  summary,
  xirrResult,
  homeCurrency = 'TWD',
  isLoading = false,
  portfolioId,
}: PerformanceMetricsProps) {
  void portfolioId;

  const displayValues = {
    totalCostHome: summary.totalCostHome,
    totalValueHome: summary.totalValueHome ?? null,
    totalUnrealizedPnlHome: summary.totalUnrealizedPnlHome ?? null,
    totalUnrealizedPnlPercentage: summary.totalUnrealizedPnlPercentage ?? null,
    xirrPercentage: xirrResult?.xirrPercentage ?? null,
    cashFlowCount: xirrResult?.cashFlowCount ?? null,
    positionCount: summary.positions.length,
  };

  // For TWD, round to integer; for others, keep 2 decimals
  const formatCurrency = (value: number | null | undefined) => {
    if (value == null) return '-';
    if (homeCurrency === 'TWD') {
      return Math.round(value).toLocaleString('zh-TW');
    }
    return value.toLocaleString('zh-TW', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    });
  };

  const formatPercent = (value: number | null | undefined) => {
    if (value == null) return '-';
    const sign = value >= 0 ? '+' : '';
    return `${sign}${value.toLocaleString('zh-TW', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    })}%`;
  };

  const pnlColor = (displayValues.totalUnrealizedPnlHome ?? 0) >= 0
    ? 'number-positive'
    : 'number-negative';

  const xirrColor = (displayValues.xirrPercentage ?? 0) >= 0
    ? 'number-positive'
    : 'number-negative';

  type PortfolioXirrDisplayState = 'ready' | 'lowConfidence' | 'unavailable';

  const hasXirrValue = displayValues.xirrPercentage != null;
  const isXirrLowConfidence = Boolean(
    xirrResult &&
    displayValues.xirrPercentage != null &&
    isXirrPeriodTooShort(xirrResult.earliestTransactionDate, xirrResult.asOfDate)
  );

  const xirrDisplayState: PortfolioXirrDisplayState = (() => {
    if (!hasXirrValue) return 'unavailable';
    if (isXirrLowConfidence) return 'lowConfidence';
    return 'ready';
  })();

  const xirrStatusText = (() => {
    if (xirrDisplayState === 'lowConfidence') {
      return '資料期間較短，年化報酬僅供參考';
    }

    if (xirrDisplayState === 'unavailable') {
      return '資料不足不顯示';
    }

    return null;
  })();

  if (isLoading) {
    return (
      <div className="card-dark p-6 animate-pulse" style={{ minHeight: 206 }}>
        <div className="h-6 bg-[var(--bg-tertiary)] rounded w-1/3 mb-4"></div>
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
          {[1, 2, 3, 4].map((i) => (
            <div key={i} className="space-y-2">
              <div className="h-4 bg-[var(--bg-tertiary)] rounded w-2/3"></div>
              <div className="h-6 bg-[var(--bg-tertiary)] rounded w-full"></div>
            </div>
          ))}
        </div>
        <div className="mt-4">
          <div className="h-4 bg-[var(--bg-tertiary)] rounded w-32"></div>
        </div>
      </div>
    );
  }

  return (
      <div className="card-dark p-6" style={{ minHeight: 206 }}>
        <h2 className="text-lg font-bold text-[var(--text-primary)] mb-4">投資組合績效</h2>

        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
          <div className="metric-card">
            <p className="text-sm text-[var(--text-muted)] mb-1">總成本</p>
            <p className="text-xl font-bold text-[var(--text-primary)] number-display">
              {formatCurrency(displayValues.totalCostHome)}
            </p>
            <p className="text-sm text-[var(--text-muted)]">{homeCurrency}</p>
          </div>

          <div className="metric-card">
            <p className="text-sm text-[var(--text-muted)] mb-1">目前市值</p>
            <p className="text-xl font-bold text-[var(--text-primary)] number-display">
              {formatCurrency(displayValues.totalValueHome)}
            </p>
            <p className="text-sm text-[var(--text-muted)]">{homeCurrency}</p>
          </div>

          <div className="metric-card">
            <p className="text-sm text-[var(--text-muted)] mb-1">未實現損益</p>
            <p className={`text-xl font-bold number-display ${pnlColor}`}>
              {formatCurrency(displayValues.totalUnrealizedPnlHome)}
            </p>
            {displayValues.totalUnrealizedPnlPercentage != null && (
              <p className={`text-sm ${pnlColor}`}>
                {formatPercent(displayValues.totalUnrealizedPnlPercentage)}
              </p>
            )}
          </div>

          <div className="metric-card">
            <p className="text-sm text-[var(--text-muted)] mb-1">年化報酬 (XIRR)</p>

            {xirrDisplayState === 'ready' && (
              <p className={`text-xl font-bold number-display ${xirrColor}`}>
                {formatPercent(displayValues.xirrPercentage)}
              </p>
            )}

            {xirrDisplayState === 'lowConfidence' && (
              <div className="space-y-1">
                <p className={`text-xl font-bold number-display ${xirrColor}`}>
                  {formatPercent(displayValues.xirrPercentage)}
                </p>
                <p className="text-sm text-[var(--color-warning)]">{xirrStatusText}</p>
              </div>
            )}

            {xirrDisplayState === 'unavailable' && (
              <div className="flex items-center gap-1 text-sm text-[var(--text-secondary)]">
                <span>{xirrStatusText}</span>
                <div className="relative group">
                  <Info className="w-4 h-4 text-[var(--text-muted)] cursor-help" />
                  <div className="absolute left-0 bottom-full mb-2 hidden group-hover:block z-10">
                    <div className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg p-2 shadow-lg text-xs text-[var(--text-secondary)] whitespace-nowrap">
                      因交易筆數或資料期間不足，暫無法可靠計算 MD／TWR／XIRR。
                    </div>
                  </div>
                </div>
              </div>
            )}

            {xirrDisplayState !== 'unavailable' &&
              displayValues.cashFlowCount != null &&
              displayValues.cashFlowCount > 1 && (
                <p className="text-sm text-[var(--text-muted)]">
                  基於 {displayValues.cashFlowCount - 1} 筆交易計算
                </p>
            )}
          </div>
        </div>

        <div className="mt-4 text-sm text-[var(--text-muted)]">
          <p>持倉數量: {displayValues.positionCount}</p>
        </div>
      </div>
  );
}
