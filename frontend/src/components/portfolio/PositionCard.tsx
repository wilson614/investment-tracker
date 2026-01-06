import type { StockPosition } from '../../types';

interface PositionCardProps {
  position: StockPosition;
  homeCurrency?: string;
}

export function PositionCard({ position, homeCurrency = 'TWD' }: PositionCardProps) {
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
    ? 'text-green-600'
    : 'text-red-600';

  return (
    <div className="bg-white rounded-lg shadow p-4 hover:shadow-md transition-shadow">
      <div className="flex justify-between items-start mb-3">
        <h3 className="text-lg font-bold text-gray-900">{position.ticker}</h3>
        <span className="text-sm text-gray-500">
          {formatNumber(position.totalShares, 4)} shares
        </span>
      </div>

      <div className="space-y-2 text-sm">
        <div className="flex justify-between">
          <span className="text-gray-600">Average Cost:</span>
          <span className="font-medium">
            {formatNumber(position.averageCostPerShare)} {homeCurrency}
          </span>
        </div>

        <div className="flex justify-between">
          <span className="text-gray-600">Total Cost:</span>
          <span className="font-medium">
            {formatNumber(position.totalCostHome)} {homeCurrency}
          </span>
        </div>

        {position.currentValueHome !== undefined && (
          <>
            <hr className="my-2" />

            <div className="flex justify-between">
              <span className="text-gray-600">Current Value:</span>
              <span className="font-medium">
                {formatNumber(position.currentValueHome)} {homeCurrency}
              </span>
            </div>

            <div className="flex justify-between">
              <span className="text-gray-600">Unrealized P&L:</span>
              <span className={`font-medium ${pnlColor}`}>
                {formatNumber(position.unrealizedPnlHome ?? 0)} {homeCurrency}
                {position.unrealizedPnlPercentage !== undefined && (
                  <span className="ml-1 text-xs">
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
