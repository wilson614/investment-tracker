import { useState, useEffect } from 'react';
import { ChevronDown, Plus, Globe, DollarSign } from 'lucide-react';
import type { Portfolio } from '../../types';
import { PortfolioType } from '../../types';
import { portfolioApi } from '../../services/api';

interface PortfolioSelectorProps {
  currentPortfolioId: string | null;
  onPortfolioChange: (portfolioId: string) => void;
  onCreateNew: () => void;
}

export function PortfolioSelector({
  currentPortfolioId,
  onPortfolioChange,
  onCreateNew,
}: PortfolioSelectorProps) {
  const [portfolios, setPortfolios] = useState<Portfolio[]>([]);
  const [isOpen, setIsOpen] = useState(false);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    loadPortfolios();
  }, []);

  const loadPortfolios = async () => {
    try {
      setIsLoading(true);
      const data = await portfolioApi.getAll();
      setPortfolios(data);
    } catch (error) {
      console.error('Failed to load portfolios:', error);
    } finally {
      setIsLoading(false);
    }
  };

  const currentPortfolio = portfolios.find((p) => p.id === currentPortfolioId);

  const getPortfolioIcon = (portfolioType: PortfolioType) => {
    return portfolioType === PortfolioType.ForeignCurrency ? (
      <Globe className="w-4 h-4 text-[var(--accent-teal)]" />
    ) : (
      <DollarSign className="w-4 h-4 text-[var(--accent-butter)]" />
    );
  };

  const getPortfolioLabel = (portfolio: Portfolio) => {
    const displayName = portfolio.displayName || portfolio.description || '投資組合';
    const currencyLabel =
      portfolio.portfolioType === PortfolioType.ForeignCurrency
        ? ` (${portfolio.baseCurrency})`
        : ` (${portfolio.baseCurrency}→${portfolio.homeCurrency})`;
    return displayName + currencyLabel;
  };

  if (isLoading) {
    return (
      <div className="h-10 w-48 bg-[var(--bg-secondary)] rounded-lg animate-pulse" />
    );
  }

  return (
    <div className="relative">
      <button
        onClick={() => setIsOpen(!isOpen)}
        className="flex items-center gap-2 px-4 py-2 bg-[var(--bg-secondary)] hover:bg-[var(--bg-tertiary)] rounded-lg border border-[var(--border-primary)] transition-colors"
      >
        {currentPortfolio && getPortfolioIcon(currentPortfolio.portfolioType)}
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
                  key={portfolio.id}
                  onClick={() => {
                    onPortfolioChange(portfolio.id);
                    setIsOpen(false);
                  }}
                  className={`w-full flex items-center gap-3 px-4 py-3 text-left hover:bg-[var(--bg-tertiary)] transition-colors ${
                    portfolio.id === currentPortfolioId
                      ? 'bg-[var(--bg-tertiary)]'
                      : ''
                  }`}
                >
                  {getPortfolioIcon(portfolio.portfolioType)}
                  <div className="flex-1 min-w-0">
                    <div className="text-sm font-medium text-[var(--text-primary)] truncate">
                      {portfolio.displayName || portfolio.description || '投資組合'}
                    </div>
                    <div className="text-xs text-[var(--text-muted)]">
                      {portfolio.portfolioType === PortfolioType.ForeignCurrency
                        ? `外幣 · ${portfolio.baseCurrency}`
                        : `主要 · ${portfolio.baseCurrency}/${portfolio.homeCurrency}`}
                    </div>
                  </div>
                </button>
              ))}
            </div>
            <div className="border-t border-[var(--border-primary)]">
              <button
                onClick={() => {
                  onCreateNew();
                  setIsOpen(false);
                }}
                className="w-full flex items-center gap-3 px-4 py-3 text-left hover:bg-[var(--bg-tertiary)] transition-colors text-[var(--accent-teal)]"
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
