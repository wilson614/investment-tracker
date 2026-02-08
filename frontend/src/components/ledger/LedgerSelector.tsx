import { useState } from 'react';
import { ChevronDown, DollarSign } from 'lucide-react';
import { useLedger } from '../../contexts/LedgerContext';
import { CURRENCY_LABELS } from '../../constants/currencies';

const getCurrencyLabel = (code: string) => CURRENCY_LABELS[code] || code;

interface LedgerSelectorProps {
  className?: string;
  onLedgerChange?: (ledgerId: string) => void;
}

export function LedgerSelector({ className = '', onLedgerChange }: LedgerSelectorProps) {
  const { ledgers, currentLedgerId, selectLedger, isLoading } = useLedger();
  const [isOpen, setIsOpen] = useState(false);

  const currentLedger = ledgers.find((ledger) => ledger.ledger.id === currentLedgerId);

  const sortedLedgers = [...ledgers].sort((a, b) =>
    a.ledger.currencyCode.localeCompare(b.ledger.currencyCode)
  );

  if (isLoading && ledgers.length === 0) {
    return (
      <div className="h-10 w-48 bg-[var(--bg-secondary)] rounded-lg animate-pulse" />
    );
  }

  return (
    <div className={`relative ${className}`}>
      <button
        type="button"
        onClick={() => setIsOpen(!isOpen)}
        className="flex items-center gap-2 px-4 py-2 bg-[var(--bg-secondary)] hover:bg-[var(--bg-tertiary)] rounded-lg border border-[var(--border-primary)] transition-colors"
      >
        <DollarSign className="w-4 h-4 text-[var(--accent-butter)]" />
        <span className="text-[var(--text-primary)] text-sm font-medium">
          {currentLedger ? getCurrencyLabel(currentLedger.ledger.currencyCode) : '選擇帳本'}
        </span>
        <ChevronDown
          className={`w-4 h-4 text-[var(--text-secondary)] transition-transform ${
            isOpen ? 'rotate-180' : ''
          }`}
        />
      </button>

      {isOpen && (
        <>
          <div
            className="fixed inset-0 z-40"
            onClick={() => setIsOpen(false)}
          />
          <div className="absolute top-full left-0 mt-1 w-72 bg-[var(--bg-secondary)] border border-[var(--border-primary)] rounded-lg shadow-lg z-50 overflow-hidden">
            <div className="max-h-64 overflow-y-auto">
              {ledgers.length === 0 ? (
                <div className="px-4 py-3 text-sm text-[var(--text-muted)]">尚無帳本</div>
              ) : (
                sortedLedgers.map((ledgerSummary) => (
                  <button
                    type="button"
                    key={ledgerSummary.ledger.id}
                    onClick={() => {
                      selectLedger(ledgerSummary.ledger.id);
                      onLedgerChange?.(ledgerSummary.ledger.id);
                      setIsOpen(false);
                    }}
                    className={`w-full flex items-center gap-3 px-4 py-3 text-left hover:bg-[var(--bg-tertiary)] transition-colors ${
                      ledgerSummary.ledger.id === currentLedgerId
                        ? 'bg-[var(--bg-tertiary)]'
                        : ''
                    }`}
                  >
                    <DollarSign className="w-4 h-4 text-[var(--accent-butter)]" />
                    <div className="flex-1 min-w-0 text-sm font-medium text-[var(--text-primary)] truncate">
                      {getCurrencyLabel(ledgerSummary.ledger.currencyCode)}
                    </div>
                  </button>
                ))
              )}
            </div>
          </div>
        </>
      )}
    </div>
  );
}
