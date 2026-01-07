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
    ? 'text-green-600'
    : 'text-red-600';

  const xirrColor = (xirrResult?.xirrPercentage ?? 0) >= 0
    ? 'text-green-600'
    : 'text-red-600';

  if (isLoading) {
    return (
      <div className="bg-white rounded-lg shadow p-6 animate-pulse">
        <div className="h-6 bg-gray-200 rounded w-1/3 mb-4"></div>
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
          {[1, 2, 3, 4].map((i) => (
            <div key={i} className="space-y-2">
              <div className="h-4 bg-gray-200 rounded w-2/3"></div>
              <div className="h-6 bg-gray-200 rounded w-full"></div>
            </div>
          ))}
        </div>
      </div>
    );
  }

  return (
    <div className="bg-white rounded-lg shadow p-6">
      <h2 className="text-xl font-bold text-gray-900 mb-4">投資組合績效</h2>

      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <div className="p-4 bg-gray-50 rounded-lg">
          <p className="text-sm text-gray-600">總成本</p>
          <p className="text-lg font-bold text-gray-900">
            {formatNumber(summary.totalCostHome)} {homeCurrency}
          </p>
        </div>

        <div className="p-4 bg-gray-50 rounded-lg">
          <p className="text-sm text-gray-600">目前市值</p>
          <p className="text-lg font-bold text-gray-900">
            {summary.totalValueHome != null
              ? `${formatNumber(summary.totalValueHome)} ${homeCurrency}`
              : '-'}
          </p>
        </div>

        <div className="p-4 bg-gray-50 rounded-lg">
          <p className="text-sm text-gray-600">未實現損益</p>
          <p className={`text-lg font-bold ${pnlColor}`}>
            {summary.totalUnrealizedPnlHome != null
              ? `${formatNumber(summary.totalUnrealizedPnlHome)} ${homeCurrency}`
              : '-'}
          </p>
          {summary.totalUnrealizedPnlPercentage != null && (
            <p className={`text-sm ${pnlColor}`}>
              {formatPercent(summary.totalUnrealizedPnlPercentage)}
            </p>
          )}
        </div>

        <div className="p-4 bg-gray-50 rounded-lg">
          <p className="text-sm text-gray-600">年化報酬率 (XIRR)</p>
          {xirrResult?.xirrPercentage != null ? (
            <p className={`text-lg font-bold ${xirrColor}`}>
              {formatPercent(xirrResult.xirrPercentage)}
            </p>
          ) : (
            <p className="text-lg font-bold text-gray-400">-</p>
          )}
          {xirrResult && (
            <p className="text-xs text-gray-500">
              基於 {xirrResult.cashFlowCount} 筆交易計算
            </p>
          )}
        </div>
      </div>

      <div className="mt-4 text-xs text-gray-500">
        <p>持倉數量: {summary.positions.length}</p>
      </div>
    </div>
  );
}
