import { useState } from 'react';
import { ChevronDown, DollarSign } from 'lucide-react';
import { CURRENCY_LABELS } from '../../constants';
import { useLedger } from '../../contexts/LedgerContext';
import type { CurrencyLedgerSummary } from '../../types';

interface LedgerSelectorProps {
  className?: string;
  onLedgerChange?: (ledgerId: string) => void;
}

export function LedgerSelector({ className = '', onLedgerChange }: LedgerSelectorProps) {
  const { ledgers, currentLedgerId, selectLedger, isLoading } = useLedger();
  const [isOpen, setIsOpen] = useState(false);

  const currentLedger = ledgers.find((ledger) => ledger.ledger.id === currentLedgerId);

  const groupedLedgers = ledgers.reduce<Record<string, CurrencyLedgerSummary[]>>((groups, ledgerSummary) => {
    const currencyCode = ledgerSummary.ledger.currencyCode;
    if (!groups[currencyCode]) {
      groups[currencyCode] = [];
    }
    groups[currencyCode].push(ledgerSummary);
    return groups;
  }, {});

  const sortedCurrencyCodes = Object.keys(groupedLedgers).sort((a, b) => a.localeCompare(b));

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
          {currentLedger ? currentLedger.ledger.name : '選擇帳本'}
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
                sortedCurrencyCodes.map((currencyCode) => (
                  <div key={currencyCode}>
                    <div className="px-4 py-2 text-xs font-medium text-[var(--text-secondary)] bg-[var(--bg-primary)] border-b border-[var(--border-primary)]">
                      {CURRENCY_LABELS[currencyCode] || currencyCode}
                    </div>
                    {groupedLedgers[currencyCode].map((ledgerSummary) => (
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
                        <div className="flex-1 min-w-0">
                          <div className="text-sm font-medium text-[var(--text-primary)] truncate">
                            {ledgerSummary.ledger.name}
                          </div>
                          <div className="text-xs text-[var(--text-secondary)]">
                            {ledgerSummary.ledger.currencyCode}
                          </div>
                        </div>
                      </button>
                    ))}
                  </div>
                ))
              )}
            </div>
          </div>
        </>
      )}
    </div>
  );
}
