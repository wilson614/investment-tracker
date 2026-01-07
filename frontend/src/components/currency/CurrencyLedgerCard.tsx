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
    ? 'number-positive'
    : 'number-negative';

  return (
    <div
      className="card-dark p-5 cursor-pointer hover:border-[var(--border-hover)] transition-all"
      onClick={onClick}
    >
      <div className="flex justify-between items-start mb-4">
        <div>
          <h3 className="text-lg font-bold text-[var(--accent-cream)]">{ledger.ledger.currencyCode}</h3>
          <p className="text-base text-[var(--text-muted)]">{ledger.ledger.name}</p>
        </div>
        <span className="text-lg font-bold text-[var(--accent-peach)] number-display">
          {formatNumber(ledger.balance, 2)}
        </span>
      </div>

      <div className="space-y-3 text-base">
        <div className="flex justify-between">
          <span className="text-[var(--text-muted)]">加權平均成本:</span>
          <span className="font-medium text-[var(--text-primary)] number-display">
            {formatNumber(ledger.weightedAverageCost, 4)}
          </span>
        </div>

        <div className="flex justify-between">
          <span className="text-[var(--text-muted)]">總成本:</span>
          <span className="font-medium text-[var(--text-primary)] number-display">
            {formatNumber(ledger.totalCostHome)} {ledger.ledger.homeCurrency}
          </span>
        </div>

        <hr className="border-[var(--border-color)] my-3" />

        <div className="flex justify-between">
          <span className="text-[var(--text-muted)]">已實現損益:</span>
          <span className={`font-medium number-display ${pnlColor}`}>
            {formatNumber(ledger.realizedPnl)} {ledger.ledger.homeCurrency}
          </span>
        </div>

        {ledger.currentValueHome !== undefined && ledger.currentValueHome !== null && (
          <>
            <div className="flex justify-between">
              <span className="text-[var(--text-muted)]">現值:</span>
              <span className="font-medium text-[var(--text-primary)] number-display">
                {formatNumber(ledger.currentValueHome)} {ledger.ledger.homeCurrency}
              </span>
            </div>

            {ledger.unrealizedPnlHome !== undefined && (
              <div className="flex justify-between">
                <span className="text-[var(--text-muted)]">未實現損益:</span>
                <span className={`font-medium number-display ${(ledger.unrealizedPnlHome ?? 0) >= 0 ? 'number-positive' : 'number-negative'}`}>
                  {formatNumber(ledger.unrealizedPnlHome)} {ledger.ledger.homeCurrency}
                  {ledger.unrealizedPnlPercentage !== undefined && (
                    <span className="ml-1 text-sm">
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
