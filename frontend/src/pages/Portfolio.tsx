import { useState, useEffect, useCallback, useRef } from 'react';
import { useParams, Link } from 'react-router-dom';
import { RefreshCw, Loader2 } from 'lucide-react';
import { portfolioApi, stockPriceApi } from '../services/api';
import { TransactionForm } from '../components/transactions/TransactionForm';
import { TransactionList } from '../components/transactions/TransactionList';
import { PositionCard } from '../components/portfolio/PositionCard';
import { PerformanceMetrics } from '../components/portfolio/PerformanceMetrics';
import { StockImportButton } from '../components/import';
import { FileDropdown } from '../components/common';
import { exportTransactionsToCsv } from '../services/csvExport';
import { StockMarket } from '../types';
import type { PortfolioSummary, CreateStockTransactionRequest, XirrResult, CurrentPriceInfo, StockMarket as StockMarketType, StockTransaction, StockQuoteResponse } from '../types';
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

// Cache keys for localStorage
const getPerformanceCacheKey = (portfolioId: string) => `perf_cache_${portfolioId}`;
const getQuoteCacheKey = (ticker: string) => `quote_cache_${ticker}`;

interface CachedPerformance {
  summary: PortfolioSummary;
  xirrResult: XirrResult | null;
  cachedAt: string;
}

interface CachedQuote {
  quote: StockQuoteResponse;
  updatedAt: string;
  market: StockMarketType;
}

// Load cached quotes for tickers (no time limit - always show cached, then refresh)
const loadCachedPrices = (tickers: string[]): Record<string, CurrentPriceInfo> => {
  const prices: Record<string, CurrentPriceInfo> = {};

  for (const ticker of tickers) {
    try {
      const cached = localStorage.getItem(getQuoteCacheKey(ticker));
      if (cached) {
        const data: CachedQuote = JSON.parse(cached);
        if (data.quote.exchangeRate) {
          prices[ticker] = {
            price: data.quote.price,
            exchangeRate: data.quote.exchangeRate,
          };
        }
      }
    } catch {
      // Ignore cache errors
    }
  }
  return prices;
};

export function PortfolioPage() {
  const { id: urlId } = useParams<{ id: string }>();
  const [portfolioId, setPortfolioId] = useState<string | null>(urlId ?? null);

  // Load cached performance data on init (no time limit - always show cached, then refresh)
  const loadCachedPerformance = (pid: string | null): { summary: PortfolioSummary | null; xirrResult: XirrResult | null } => {
    if (!pid) return { summary: null, xirrResult: null };
    try {
      const cached = localStorage.getItem(getPerformanceCacheKey(pid));
      if (cached) {
        const data: CachedPerformance = JSON.parse(cached);
        return { summary: data.summary, xirrResult: data.xirrResult };
      }
    } catch {
      // Ignore cache errors
    }
    return { summary: null, xirrResult: null };
  };

  const cachedPerf = loadCachedPerformance(portfolioId);
  const [summary, setSummary] = useState<PortfolioSummary | null>(cachedPerf.summary);
  const [xirrResult, setXirrResult] = useState<XirrResult | null>(cachedPerf.xirrResult);
  const [isLoading, setIsLoading] = useState(true);
  const [isCalculating, setIsCalculating] = useState(false);
  const [isFetchingAll, setIsFetchingAll] = useState(false);
  const [refreshTrigger, setRefreshTrigger] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);
  const [editingTransaction, setEditingTransaction] = useState<StockTransaction | null>(null);
  const [transactions, setTransactions] = useState<StockTransaction[]>([]);

  const currentPricesRef = useRef<Record<string, CurrentPriceInfo>>({});
  const importTriggerRef = useRef<(() => void) | null>(null);

  const loadData = useCallback(async (pid: string) => {
    try {
      setIsLoading(true);
      setError(null);
      const [basicSummary, txData] = await Promise.all([
        portfolioApi.getSummary(pid),
        transactionApi.getByPortfolio(pid),
      ]);
      setTransactions(txData);

      // Load cached prices for all positions
      const tickers = basicSummary.positions.map(pos => pos.ticker);
      const cachedPrices = loadCachedPrices(tickers);
      currentPricesRef.current = cachedPrices;

      // If we have cached prices, calculate with them immediately
      if (Object.keys(cachedPrices).length > 0) {
        const [summaryWithPrices, xirr] = await Promise.all([
          portfolioApi.getSummary(pid, cachedPrices),
          portfolioApi.calculateXirr(pid, { currentPrices: cachedPrices }),
        ]);
        setSummary(summaryWithPrices);
        setXirrResult(xirr);
      } else {
        setSummary(basicSummary);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load portfolio');
    } finally {
      setIsLoading(false);
    }
  }, []);

  const hasFetchedOnLoad = useRef(false);

  // Fetch default portfolio if no URL id provided
  useEffect(() => {
    const initializePortfolio = async () => {
      if (urlId) {
        setPortfolioId(urlId);
        loadData(urlId);
      } else {
        try {
          const portfolios = await portfolioApi.getAll();
          if (portfolios.length > 0) {
            const defaultId = portfolios[0].id;
            setPortfolioId(defaultId);
            loadData(defaultId);
          } else {
            setError('找不到投資組合');
            setIsLoading(false);
          }
        } catch (err) {
          setError(err instanceof Error ? err.message : 'Failed to load portfolios');
          setIsLoading(false);
        }
      }
    };
    initializePortfolio();
  }, [urlId, loadData]);

  // Auto-fetch all prices on page load (after summary is loaded)
  useEffect(() => {
    if (summary && !isLoading && !hasFetchedOnLoad.current && summary.positions.length > 0) {
      hasFetchedOnLoad.current = true;
      handleFetchAllPrices();
    }
  }, [summary, isLoading]);

  const handleAddTransaction = async (data: CreateStockTransactionRequest) => {
    const nextTicker = data.ticker.toUpperCase();
    const isNewPosition = !summary?.positions.some(p => p.ticker.toUpperCase() === nextTicker);

    if (editingTransaction) {
      await transactionApi.update(editingTransaction.id, {
        ticker: data.ticker,
        transactionType: data.transactionType,
        transactionDate: data.transactionDate,
        shares: data.shares,
        pricePerShare: data.pricePerShare,
        exchangeRate: data.exchangeRate ?? 1,
        fees: data.fees,
        fundSource: data.fundSource,
        currencyLedgerId: data.currencyLedgerId,
        notes: data.notes,
      });
    } else {
      await transactionApi.create(data);
    }

    // If this creates a new position, prefetch and cache quote so the PositionCard shows price immediately
    if (isNewPosition) {
      try {
        const homeCurrency = summary?.portfolio.homeCurrency ?? 'TWD';
        const market = guessMarket(nextTicker);
        let quote = await stockPriceApi.getQuoteWithRate(market, nextTicker, homeCurrency);
        let finalMarket = market;

        if (!quote && market === StockMarket.US) {
          quote = await stockPriceApi.getQuoteWithRate(StockMarket.UK, nextTicker, homeCurrency);
          if (quote) finalMarket = StockMarket.UK;
        }

        if (quote?.exchangeRate) {
          const cacheData: CachedQuote = {
            quote,
            updatedAt: new Date().toISOString(),
            market: finalMarket,
          };
          localStorage.setItem(getQuoteCacheKey(nextTicker), JSON.stringify(cacheData));
        }
      } catch {
        // Ignore prefetch errors
      }
    }

    if (!portfolioId) return;
    await loadData(portfolioId);
    setShowForm(false);
    setEditingTransaction(null);
  };

  const handleEditTransaction = (transaction: StockTransaction) => {
    setEditingTransaction(transaction);
    setShowForm(true);
  };

  const handleDeleteTransaction = async (transactionId: string) => {
    if (!window.confirm('確定要刪除此交易紀錄嗎？')) return;
    await transactionApi.delete(transactionId);
    if (portfolioId) await loadData(portfolioId);
  };

  const updateSummaryWithPrices = async (prices: Record<string, CurrentPriceInfo>) => {
    if (!portfolioId || Object.keys(prices).length === 0) return;

    setIsCalculating(true);

    try {
      const [summaryData, xirrData] = await Promise.all([
        portfolioApi.getSummary(portfolioId, prices),
        portfolioApi.calculateXirr(portfolioId, { currentPrices: prices }),
      ]);
      setSummary(summaryData);
      setXirrResult(xirrData);

      // Cache the performance data
      try {
        const cacheData: CachedPerformance = {
          summary: summaryData,
          xirrResult: xirrData,
          cachedAt: new Date().toISOString(),
        };
        localStorage.setItem(getPerformanceCacheKey(portfolioId), JSON.stringify(cacheData));
      } catch {
        // Ignore cache errors
      }
    } catch (err) {
      console.error('Failed to calculate performance:', err);
    } finally {
      setIsCalculating(false);
    }
  };

  const handlePositionPriceUpdate = useCallback((ticker: string, price: number, exchangeRate: number) => {
    currentPricesRef.current[ticker] = { price, exchangeRate };
    updateSummaryWithPrices({ ...currentPricesRef.current });
  }, [portfolioId]);

  const handleFetchAllPrices = async () => {
    if (!summary) return;

    setIsFetchingAll(true);

    try {
      const homeCurrency = summary.portfolio.homeCurrency;
      const fetchPromises = summary.positions.map(async (position) => {
        try {
          const market = guessMarket(position.ticker);
          let quote = await stockPriceApi.getQuoteWithRate(market, position.ticker, homeCurrency);
          let finalMarket = market;

          if (!quote && market === StockMarket.US) {
            quote = await stockPriceApi.getQuoteWithRate(StockMarket.UK, position.ticker, homeCurrency);
            if (quote) finalMarket = StockMarket.UK;
          }

          if (quote?.exchangeRate) {
            // Save full quote to cache for PositionCard to use
            const cacheData: CachedQuote = {
              quote,
              updatedAt: new Date().toISOString(),
              market: finalMarket,
            };
            localStorage.setItem(getQuoteCacheKey(position.ticker), JSON.stringify(cacheData));
            return { ticker: position.ticker, price: quote.price, exchangeRate: quote.exchangeRate };
          }
          return null;
        } catch {
          if (guessMarket(position.ticker) === StockMarket.US) {
            try {
              const ukQuote = await stockPriceApi.getQuoteWithRate(StockMarket.UK, position.ticker, homeCurrency);
              if (ukQuote?.exchangeRate) {
                // Save full quote to cache
                const cacheData: CachedQuote = {
                  quote: ukQuote,
                  updatedAt: new Date().toISOString(),
                  market: StockMarket.UK,
                };
                localStorage.setItem(getQuoteCacheKey(position.ticker), JSON.stringify(cacheData));
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

      // Trigger PositionCard to re-read from cache
      setRefreshTrigger(Date.now());
    } finally {
      setIsFetchingAll(false);
    }
  };

  const handleExportTransactions = () => {
    if (!summary || transactions.length === 0) return;
    exportTransactionsToCsv(
      transactions,
      summary.portfolio.baseCurrency,
      summary.portfolio.homeCurrency
    );
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
            <h1 className="text-2xl font-bold text-[var(--text-primary)]">投資組合</h1>
            {summary.portfolio.description && (
              <p className="text-[var(--text-secondary)] text-base mt-1">{summary.portfolio.description}</p>
            )}
          </div>
          <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={handleFetchAllPrices}
                disabled={isFetchingAll || isCalculating || summary.positions.length === 0}
                className="btn-dark flex items-center gap-2 px-3 py-1.5 text-sm disabled:opacity-50"
                title="更新報價"
              >
                {isFetchingAll ? (
                  <Loader2 className="w-4 h-4 animate-spin" />
                ) : (
                  <RefreshCw className="w-4 h-4" />
                )}
                更新報價
              </button>
            </div>
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
            <h2 className="text-lg font-bold text-[var(--text-primary)] mb-4">
              持倉 <span className="text-sm font-normal text-[var(--text-muted)]">（點擊查看詳情）</span>
            </h2>
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
              {summary.positions.map((position) => (
                <Link
                  key={position.ticker}
                  to={`/portfolio/position/${position.ticker}`}
                  className="block"
                >
                  <PositionCard
                    position={position}
                    baseCurrency={summary.portfolio.baseCurrency}
                    homeCurrency={summary.portfolio.homeCurrency}
                    onPriceUpdate={handlePositionPriceUpdate}
                    autoFetch={false}
                    refreshTrigger={refreshTrigger}
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

        {/* Transaction History */}
        <div className="card-dark overflow-hidden">
          <div className="px-5 py-4 border-b border-[var(--border-color)] flex justify-between items-center">
            <h2 className="text-lg font-bold text-[var(--text-primary)]">
              全部交易紀錄
            </h2>
            <div className="flex items-center gap-2">
              <FileDropdown
                onImport={() => importTriggerRef.current?.()}
                onExport={handleExportTransactions}
                exportDisabled={transactions.length === 0}
              />
              <button
                onClick={() => setShowForm(true)}
                className="btn-accent px-3 py-1.5 text-sm"
              >
                + 新增
              </button>
              <StockImportButton
                portfolioId={portfolioId!}
                onImportComplete={loadData}
                renderTrigger={(onClick) => {
                  importTriggerRef.current = onClick;
                  return null;
                }}
              />
            </div>
          </div>
          {transactions.length > 0 ? (
            <TransactionList
              transactions={transactions}
              onEdit={handleEditTransaction}
              onDelete={handleDeleteTransaction}
            />
          ) : (
            <div className="p-8 text-center text-[var(--text-muted)]">
              尚無交易紀錄
            </div>
          )}
        </div>

        {/* Add Transaction Modal */}
        {showForm && (
          <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50">
            <div className="card-dark p-0 w-full max-w-2xl max-h-[90vh] overflow-y-auto m-4">
              <TransactionForm
                portfolioId={portfolioId!}
                initialData={editingTransaction ?? undefined}
                onSubmit={handleAddTransaction}
                onCancel={() => {
                  setShowForm(false);
                  setEditingTransaction(null);
                }}
              />
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
