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

  // Format TWD as integer
  const formatTWD = (value: number | null | undefined) => {
    if (value == null) return '-';
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
          <span className="text-[var(--text-muted)]">換匯均價:</span>
          <span className="font-medium text-[var(--text-primary)] number-display">
            {formatNumber(ledger.averageExchangeRate, 4)}
          </span>
        </div>

        <div className="flex justify-between">
          <span className="text-[var(--text-muted)]">累計換匯:</span>
          <span className="font-medium text-[var(--text-primary)] number-display">
            {formatTWD(ledger.totalExchanged)} {ledger.ledger.homeCurrency}
          </span>
        </div>

        <hr className="border-[var(--border-color)] my-3" />

        <div className="flex justify-between">
          <span className="text-[var(--text-muted)]">股票投入:</span>
          <span className="font-medium text-[var(--accent-peach)] number-display">
            {formatNumber(ledger.totalSpentOnStocks, 4)} {ledger.ledger.currencyCode}
          </span>
        </div>
      </div>
    </div>
  );
}
