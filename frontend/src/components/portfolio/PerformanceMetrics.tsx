import React from 'react';
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
  // Track if we've ever loaded data - only show skeleton on initial load
  const hasLoadedRef = React.useRef(false);

  // Cache the last valid values to prevent flashing
  const lastValuesRef = React.useRef<{
    totalCostHome: number | null;
    totalValueHome: number | null;
    totalUnrealizedPnlHome: number | null;
    totalUnrealizedPnlPercentage: number | null;
    xirrPercentage: number | null;
    cashFlowCount: number | null;
    positionCount: number;
  }>({
    totalCostHome: null,
    totalValueHome: null,
    totalUnrealizedPnlHome: null,
    totalUnrealizedPnlPercentage: null,
    xirrPercentage: null,
    cashFlowCount: null,
    positionCount: 0,
  });

  if (summary) {
    hasLoadedRef.current = true;
    // Update cached values when we have new data
    lastValuesRef.current = {
      totalCostHome: summary.totalCostHome,
      totalValueHome: summary.totalValueHome ?? lastValuesRef.current.totalValueHome,
      totalUnrealizedPnlHome: summary.totalUnrealizedPnlHome ?? lastValuesRef.current.totalUnrealizedPnlHome,
      totalUnrealizedPnlPercentage: summary.totalUnrealizedPnlPercentage ?? lastValuesRef.current.totalUnrealizedPnlPercentage,
      xirrPercentage: xirrResult?.xirrPercentage ?? lastValuesRef.current.xirrPercentage,
      cashFlowCount: xirrResult?.cashFlowCount ?? lastValuesRef.current.cashFlowCount,
      positionCount: summary.positions.length,
    };
  }

  // Use cached values for display
  const displayValues = lastValuesRef.current;

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

  if (isLoading && !hasLoadedRef.current) {
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
          <p className="text-sm text-[var(--text-muted)] mb-1">年化報酬率 (XIRR)</p>
          <p className={`text-xl font-bold number-display ${displayValues.xirrPercentage != null ? xirrColor : 'text-[var(--text-muted)]'}`}>
            {formatPercent(displayValues.xirrPercentage)}
          </p>
          {displayValues.cashFlowCount != null && (
            <p className="text-sm text-[var(--text-muted)]">
              基於 {displayValues.cashFlowCount} 筆交易計算
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
