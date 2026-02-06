import { useState } from 'react';
import { ChevronDown, Plus, DollarSign } from 'lucide-react';
import type { Portfolio } from '../../types';
import { usePortfolio } from '../../contexts/PortfolioContext';
import { COMMON_CURRENCIES, CURRENCY_LABELS } from '../../constants';

interface PortfolioSelectorProps {
  onCreateNew: () => void;
  className?: string;
}

export function PortfolioSelector({
  onCreateNew,
  className = '',
}: PortfolioSelectorProps) {
  const { portfolios, currentPortfolioId, selectPortfolio, isLoading } = usePortfolio();
  const [isOpen, setIsOpen] = useState(false);

  const currentPortfolio = portfolios.find((p) => p.id === currentPortfolioId);

  const portfolioCurrencies = new Set(portfolios.map((p) => p.baseCurrency));
  const allCurrenciesHavePortfolios = COMMON_CURRENCIES.every((currency) =>
    portfolioCurrencies.has(currency)
  );

  const getPortfolioLabel = (portfolio: Portfolio) => {
    return CURRENCY_LABELS[portfolio.baseCurrency] || portfolio.baseCurrency;
  };

  if (isLoading && portfolios.length === 0) {
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
          {currentPortfolio ? getPortfolioLabel(currentPortfolio) : '選擇投資組合'}
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
              {portfolios.map((portfolio) => (
                <button
                  type="button"
                  key={portfolio.id}
                  onClick={() => {
                    selectPortfolio(portfolio.id);
                    setIsOpen(false);
                  }}
                  className={`w-full flex items-center gap-3 px-4 py-3 text-left hover:bg-[var(--bg-tertiary)] transition-colors ${
                    portfolio.id === currentPortfolioId
                      ? 'bg-[var(--bg-tertiary)]'
                      : ''
                  }`}
                >
                  <DollarSign className="w-4 h-4 text-[var(--accent-butter)]" />
                  <div className="flex-1 min-w-0">
                    <div className="text-sm font-medium text-[var(--text-primary)] truncate">
                      {getPortfolioLabel(portfolio)}
                    </div>
                  </div>
                </button>
              ))}
            </div>
            <div className="border-t border-[var(--border-primary)]">
              <button
                type="button"
                onClick={() => {
                  if (allCurrenciesHavePortfolios) return;
                  onCreateNew();
                  setIsOpen(false);
                }}
                disabled={allCurrenciesHavePortfolios}
                className={`w-full flex items-center gap-3 px-4 py-3 text-left transition-colors text-[var(--accent-peach)] ${
                  allCurrenciesHavePortfolios
                    ? 'opacity-50 cursor-not-allowed'
                    : 'hover:bg-[var(--bg-tertiary)]'
                }`}
              >
                <Plus className="w-4 h-4" />
                <span className="text-sm font-medium">新增投資組合</span>
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  );
}
