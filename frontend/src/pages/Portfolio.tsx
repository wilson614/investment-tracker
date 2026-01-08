import { useState, useEffect, useCallback, useRef } from 'react';
import { useParams, Link } from 'react-router-dom';
import { Pencil, RefreshCw, Loader2 } from 'lucide-react';
import { portfolioApi, stockPriceApi } from '../services/api';
import { TransactionForm } from '../components/transactions/TransactionForm';
import { PositionCard } from '../components/portfolio/PositionCard';
import { PerformanceMetrics } from '../components/portfolio/PerformanceMetrics';
import { StockImportButton } from '../components/import';
import { StockMarket } from '../types';
import type { PortfolioSummary, CreateStockTransactionRequest, XirrResult, CurrentPriceInfo, StockMarket as StockMarketType } from '../types';
import { transactionApi } from '../services/api';

const guessMarket = (ticker: string): StockMarketType => {
  if (/^\d+[A-Za-z]*$/.test(ticker)) {
    return StockMarket.TW;
  }
  if (ticker.endsWith('.L')) {
    return StockMarket.UK;
  }
  return StockMarket.US;
};

export function PortfolioPage() {
  const { id } = useParams<{ id: string }>();
  const [summary, setSummary] = useState<PortfolioSummary | null>(null);
  const [xirrResult, setXirrResult] = useState<XirrResult | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isCalculating, setIsCalculating] = useState(false);
  const [isFetchingAll, setIsFetchingAll] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);
  const [isEditingName, setIsEditingName] = useState(false);
  const [editName, setEditName] = useState('');
  const [editDescription, setEditDescription] = useState('');

  const currentPricesRef = useRef<Record<string, CurrentPriceInfo>>({});

  const loadData = useCallback(async () => {
    if (!id) return;

    try {
      setIsLoading(true);
      setError(null);
      const summaryData = await portfolioApi.getSummary(id);
      setSummary(summaryData);
      currentPricesRef.current = {};
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load portfolio');
    } finally {
      setIsLoading(false);
    }
  }, [id]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleAddTransaction = async (data: CreateStockTransactionRequest) => {
    await transactionApi.create(data);
    await loadData();
    setShowForm(false);
  };

  const updateSummaryWithPrices = async (prices: Record<string, CurrentPriceInfo>) => {
    if (!id || Object.keys(prices).length === 0) return;

    setIsCalculating(true);

    try {
      const [summaryData, xirrData] = await Promise.all([
        portfolioApi.getSummary(id, prices),
        portfolioApi.calculateXirr(id, { currentPrices: prices }),
      ]);
      setSummary(summaryData);
      setXirrResult(xirrData);
    } catch (err) {
      console.error('Failed to calculate performance:', err);
    } finally {
      setIsCalculating(false);
    }
  };

  const handlePositionPriceUpdate = useCallback((ticker: string, price: number, exchangeRate: number) => {
    currentPricesRef.current[ticker] = { price, exchangeRate };
    updateSummaryWithPrices({ ...currentPricesRef.current });
  }, [id]);

  const handleFetchAllPrices = async () => {
    if (!summary) return;

    setIsFetchingAll(true);

    try {
      const homeCurrency = summary.portfolio.homeCurrency;
      const fetchPromises = summary.positions.map(async (position) => {
        try {
          const market = guessMarket(position.ticker);
          let quote = await stockPriceApi.getQuoteWithRate(market, position.ticker, homeCurrency);

          if (!quote && market === StockMarket.US) {
            quote = await stockPriceApi.getQuoteWithRate(StockMarket.UK, position.ticker, homeCurrency);
          }

          if (quote?.exchangeRate) {
            return { ticker: position.ticker, price: quote.price, exchangeRate: quote.exchangeRate };
          }
          return null;
        } catch {
          if (guessMarket(position.ticker) === StockMarket.US) {
            try {
              const ukQuote = await stockPriceApi.getQuoteWithRate(StockMarket.UK, position.ticker, homeCurrency);
              if (ukQuote?.exchangeRate) {
                return { ticker: position.ticker, price: ukQuote.price, exchangeRate: ukQuote.exchangeRate };
              }
            } catch {
              // UK also failed
            }
          }
          console.error(`Failed to fetch price for ${position.ticker}`);
          return null;
        }
      });

      const results = await Promise.all(fetchPromises);
      const newPrices: Record<string, CurrentPriceInfo> = {};

      results.forEach((result) => {
        if (result) {
          newPrices[result.ticker] = { price: result.price, exchangeRate: result.exchangeRate };
        }
      });

      currentPricesRef.current = { ...currentPricesRef.current, ...newPrices };

      if (Object.keys(newPrices).length > 0) {
        await updateSummaryWithPrices({ ...currentPricesRef.current });
      }
    } finally {
      setIsFetchingAll(false);
    }
  };

  const handleStartEditName = () => {
    if (summary) {
      setEditName(summary.portfolio.name);
      setEditDescription(summary.portfolio.description ?? '');
      setIsEditingName(true);
    }
  };

  const handleSaveName = async () => {
    if (!summary || !editName.trim()) return;
    try {
      await portfolioApi.update(summary.portfolio.id, {
        name: editName.trim(),
        description: editDescription.trim() || undefined
      });
      setIsEditingName(false);
      await loadData();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update');
    }
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-[var(--text-muted)] text-lg">載入中...</div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-[var(--color-danger)] text-lg">{error}</div>
      </div>
    );
  }

  if (!summary) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-[var(--text-muted)] text-lg">找不到投資組合</div>
      </div>
    );
  }

  return (
    <div className="min-h-screen py-8">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        {/* Header */}
        <div className="flex justify-between items-start mb-6">
          <div>
            {isEditingName ? (
              <div className="space-y-3">
                <input
                  type="text"
                  value={editName}
                  onChange={(e) => setEditName(e.target.value)}
                  className="input-dark text-2xl font-bold w-full"
                  autoFocus
                  placeholder="組合名稱"
                />
                <input
                  type="text"
                  value={editDescription}
                  onChange={(e) => setEditDescription(e.target.value)}
                  className="input-dark w-full"
                  placeholder="描述（選填）"
                />
                <div className="flex gap-2">
                  <button onClick={handleSaveName} className="btn-accent py-1 px-4">儲存</button>
                  <button onClick={() => setIsEditingName(false)} className="btn-dark py-1 px-4">取消</button>
                </div>
              </div>
            ) : (
              <>
                <div className="flex items-center gap-2">
                  <h1 className="text-2xl font-bold text-[var(--text-primary)]">
                    {summary.portfolio.name}
                  </h1>
                  <button
                    onClick={handleStartEditName}
                    className="p-1 text-[var(--text-muted)] hover:text-[var(--accent-butter)] hover:bg-[var(--bg-hover)] rounded transition-colors"
                    title="編輯名稱"
                  >
                    <Pencil className="w-4 h-4" />
                  </button>
                </div>
                {summary.portfolio.description && (
                  <p className="text-[var(--text-secondary)] text-base mt-1">{summary.portfolio.description}</p>
                )}
                <p className="text-sm text-[var(--text-muted)] mt-1">
                  {summary.portfolio.baseCurrency} → {summary.portfolio.homeCurrency}
                </p>
              </>
            )}
          </div>
          {!isEditingName && (
            <div className="flex items-center gap-2">
              <button
                onClick={() => setShowForm(true)}
                className="btn-accent px-3 py-1.5 text-sm"
              >
                + 新增交易
              </button>
              <StockImportButton
                portfolioId={id!}
                onImportComplete={loadData}
                compact
              />
            </div>
          )}
        </div>

        {/* Performance Metrics - horizontal layout */}
        <div className="mb-6">
          <PerformanceMetrics
            summary={summary}
            xirrResult={xirrResult}
            homeCurrency={summary.portfolio.homeCurrency}
            isLoading={isCalculating}
          />
        </div>

        {/* Positions */}
        {summary.positions.length > 0 ? (
          <div className="mb-6">
            <div className="flex items-center justify-between mb-4">
              <h2 className="text-lg font-bold text-[var(--text-primary)]">
                持倉 <span className="text-sm font-normal text-[var(--text-muted)]">（點擊查看詳情）</span>
              </h2>
              <button
                type="button"
                onClick={handleFetchAllPrices}
                disabled={isFetchingAll || isCalculating}
                className="btn-dark flex items-center gap-2 px-3 py-1.5 text-sm disabled:opacity-50"
              >
                {isFetchingAll ? (
                  <Loader2 className="w-4 h-4 animate-spin" />
                ) : (
                  <RefreshCw className="w-4 h-4" />
                )}
                獲取全部報價
              </button>
            </div>
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
              {summary.positions.map((position) => (
                <Link
                  key={position.ticker}
                  to={`/portfolio/${id}/position/${position.ticker}`}
                  className="block"
                >
                  <PositionCard
                    position={position}
                    baseCurrency={summary.portfolio.baseCurrency}
                    homeCurrency={summary.portfolio.homeCurrency}
                    onPriceUpdate={handlePositionPriceUpdate}
                  />
                </Link>
              ))}
            </div>
          </div>
        ) : (
          <div className="card-dark p-8 text-center">
            <p className="text-[var(--text-muted)]">尚無持倉，請新增交易</p>
          </div>
        )}

        {/* Add Transaction Modal */}
        {showForm && (
          <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50">
            <div className="card-dark p-0 w-full max-w-2xl max-h-[90vh] overflow-y-auto m-4">
              <TransactionForm
                portfolioId={id!}
                onSubmit={handleAddTransaction}
                onCancel={() => setShowForm(false)}
              />
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
