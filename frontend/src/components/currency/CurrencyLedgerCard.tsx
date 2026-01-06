import type { CurrencyLedgerSummary } from '../../types';

interface CurrencyLedgerCardProps {
  ledger: CurrencyLedgerSummary;
  onClick?: () => void;
}

export function CurrencyLedgerCard({ ledger, onClick }: CurrencyLedgerCardProps) {
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

  const pnlColor = (ledger.realizedPnl ?? 0) >= 0
    ? 'text-green-600'
    : 'text-red-600';

  return (
    <div
      className="bg-white rounded-lg shadow p-4 hover:shadow-md transition-shadow cursor-pointer"
      onClick={onClick}
    >
      <div className="flex justify-between items-start mb-3">
        <div>
          <h3 className="text-lg font-bold text-gray-900">{ledger.ledger.currencyCode}</h3>
          <p className="text-sm text-gray-500">{ledger.ledger.name}</p>
        </div>
        <span className="text-lg font-semibold text-blue-600">
          {formatNumber(ledger.balance, 2)} {ledger.ledger.currencyCode}
        </span>
      </div>

      <div className="space-y-2 text-sm">
        <div className="flex justify-between">
          <span className="text-gray-600">加權平均成本:</span>
          <span className="font-medium">
            {formatNumber(ledger.weightedAverageCost, 4)} {ledger.ledger.homeCurrency}
          </span>
        </div>

        <div className="flex justify-between">
          <span className="text-gray-600">總成本:</span>
          <span className="font-medium">
            {formatNumber(ledger.totalCostHome)} {ledger.ledger.homeCurrency}
          </span>
        </div>

        <hr className="my-2" />

        <div className="flex justify-between">
          <span className="text-gray-600">已實現損益:</span>
          <span className={`font-medium ${pnlColor}`}>
            {formatNumber(ledger.realizedPnl)} {ledger.ledger.homeCurrency}
          </span>
        </div>

        {ledger.currentValueHome !== undefined && ledger.currentValueHome !== null && (
          <>
            <div className="flex justify-between">
              <span className="text-gray-600">現值:</span>
              <span className="font-medium">
                {formatNumber(ledger.currentValueHome)} {ledger.ledger.homeCurrency}
              </span>
            </div>

            {ledger.unrealizedPnlHome !== undefined && (
              <div className="flex justify-between">
                <span className="text-gray-600">未實現損益:</span>
                <span className={`font-medium ${(ledger.unrealizedPnlHome ?? 0) >= 0 ? 'text-green-600' : 'text-red-600'}`}>
                  {formatNumber(ledger.unrealizedPnlHome)} {ledger.ledger.homeCurrency}
                  {ledger.unrealizedPnlPercentage !== undefined && (
                    <span className="ml-1 text-xs">
                      ({formatPercent(ledger.unrealizedPnlPercentage)})
                    </span>
                  )}
                </span>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}
