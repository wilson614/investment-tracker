import type { CurrencyLedgerSummary } from '../../types';

interface CurrencyLedgerCardProps {
  ledger: CurrencyLedgerSummary;
  onClick?: () => void;
}

export function CurrencyLedgerCard({ ledger, onClick }: CurrencyLedgerCardProps) {
  const formatNumber = (value: number | null | undefined, decimals = 2) => {
    if (value == null || isNaN(value)) return '-';
    return value.toLocaleString('zh-TW', {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    });
  };

  // Format TWD as integer
  const formatTWD = (value: number | null | undefined) => {
    if (value == null || isNaN(value)) return '-';
    return Math.round(value).toLocaleString('zh-TW');
  };

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
          <span className="text-[var(--text-muted)]">目前成本:</span>
          <span className="font-medium text-[var(--text-primary)] number-display">
            {formatTWD(ledger.totalCost)} {ledger.ledger.homeCurrency}
          </span>
        </div>

        <div className="flex justify-between">
          <span className="text-[var(--text-muted)]">換匯均價:</span>
          <span className="font-medium text-[var(--text-primary)] number-display">
            {formatNumber(ledger.averageExchangeRate, 4)}
          </span>
        </div>

        <hr className="border-[var(--border-color)] my-3" />

        <div className="flex justify-between">
          <span className="text-[var(--text-muted)]">已實現損益:</span>
          <span className={`font-medium number-display ${(ledger.realizedPnl ?? 0) >= 0 ? 'number-positive' : 'number-negative'}`}>
            {(ledger.realizedPnl ?? 0) >= 0 ? '+' : ''}{formatTWD(ledger.realizedPnl)} {ledger.ledger.homeCurrency}
          </span>
        </div>

        {(ledger.totalInterest ?? 0) > 0 && (
          <div className="flex justify-between">
            <span className="text-[var(--text-muted)]">利息收入:</span>
            <span className="font-medium text-[var(--accent-peach)] number-display">
              {formatNumber(ledger.totalInterest, 2)} {ledger.ledger.currencyCode}
            </span>
          </div>
        )}
      </div>
    </div>
  );
}
