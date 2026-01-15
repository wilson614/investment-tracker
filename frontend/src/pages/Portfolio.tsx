import { useState, useEffect, useCallback, useRef } from 'react';
import { Link } from 'react-router-dom';
import { RefreshCw, Loader2 } from 'lucide-react';
import { portfolioApi, stockPriceApi, marketDataApi } from '../services/api';
import { TransactionForm } from '../components/transactions/TransactionForm';
import { TransactionList } from '../components/transactions/TransactionList';
import { PositionCard } from '../components/portfolio/PositionCard';
import { PerformanceMetrics } from '../components/portfolio/PerformanceMetrics';
import { PortfolioSelector } from '../components/portfolio/PortfolioSelector';
import { CreatePortfolioForm } from '../components/portfolio/CreatePortfolioForm';
import { StockImportButton } from '../components/import';
import { FileDropdown } from '../components/common';
import { exportTransactionsToCsv, exportPositionsToCsv } from '../services/csvExport';
import { StockMarket, TransactionType, PortfolioType } from '../types';
import { isEuronextSymbol, getEuronextSymbol } from '../constants';
import type { Portfolio, PortfolioSummary, CreateStockTransactionRequest, XirrResult, CurrentPriceInfo, StockMarket as StockMarketType, StockTransaction, StockQuoteResponse } from '../types';
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

// Cache keys for localStorage - include portfolio ID to prevent cross-account data leakage
const getPerfCacheKey = (portfolioId: string) => `perf_cache_${portfolioId}`;
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
  const [portfolio, setPortfolio] = useState<Portfolio | null>(null);

  // Don't load cache on init - wait until we know the portfolio ID
  // Note: We use loadCachedPrices (individual quote cache) instead of loadCachedPerformance
  // because it's more accurate - we recalculate summary/XIRR with the cached prices
  const [summary, setSummary] = useState<PortfolioSummary | null>(null);
  const [xirrResult, setXirrResult] = useState<XirrResult | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isCalculating, setIsCalculating] = useState(false);
  const [isFetchingAll, setIsFetchingAll] = useState(false);
  const [refreshTrigger, setRefreshTrigger] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);
  const [showCreatePortfolio, setShowCreatePortfolio] = useState(false);
  const [editingTransaction, setEditingTransaction] = useState<StockTransaction | null>(null);
  const [transactions, setTransactions] = useState<StockTransaction[]>([]);

  const currentPricesRef = useRef<Record<string, CurrentPriceInfo>>({});
  const importTriggerRef = useRef<(() => void) | null>(null);
  // Store current portfolio ID for switching
  const [currentPortfolioId, setCurrentPortfolioId] = useState<string | null>(null);

  const loadDataForPortfolio = useCallback(async (portfolioId: string) => {
    try {
      setIsLoading(true);
      setError(null);

      const currentPortfolio = await portfolioApi.getById(portfolioId);
      setPortfolio(currentPortfolio);
      setCurrentPortfolioId(portfolioId);

      const [basicSummary, txData] = await Promise.all([
        portfolioApi.getSummary(portfolioId),
        transactionApi.getByPortfolio(portfolioId),
      ]);
      setTransactions(txData);

      // Load cached prices for all positions
      const tickers = basicSummary.positions.map(pos => pos.ticker);
      const cachedPrices = loadCachedPrices(tickers);
      currentPricesRef.current = cachedPrices;

      // If we have cached prices, calculate with them immediately
      if (Object.keys(cachedPrices).length > 0) {
        const [summaryWithPrices, xirr] = await Promise.all([
          portfolioApi.getSummary(portfolioId, cachedPrices),
          portfolioApi.calculateXirr(portfolioId, { currentPrices: cachedPrices }),
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

  const loadData = useCallback(async () => {
    try {
      setIsLoading(true);
      setError(null);

      // Get or create user's portfolio
      let portfolios = await portfolioApi.getAll();
      let currentPortfolio: Portfolio;

      if (portfolios.length === 0) {
        // Create default portfolio for new users
        currentPortfolio = await portfolioApi.create({
          baseCurrency: 'USD',
          homeCurrency: 'TWD',
        });
      } else {
        currentPortfolio = portfolios[0];
      }

      setCurrentPortfolioId(currentPortfolio.id);

      setPortfolio(currentPortfolio);
      const portfolioId = currentPortfolio.id;

      const [basicSummary, txData] = await Promise.all([
        portfolioApi.getSummary(portfolioId),
        transactionApi.getByPortfolio(portfolioId),
      ]);
      setTransactions(txData);

      // Load cached prices for all positions
      const tickers = basicSummary.positions.map(pos => pos.ticker);
      const cachedPrices = loadCachedPrices(tickers);
      currentPricesRef.current = cachedPrices;

      // If we have cached prices, calculate with them immediately
      if (Object.keys(cachedPrices).length > 0) {
        const [summaryWithPrices, xirr] = await Promise.all([
          portfolioApi.getSummary(portfolioId, cachedPrices),
          portfolioApi.calculateXirr(portfolioId, { currentPrices: cachedPrices }),
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

  useEffect(() => {
    loadData();
  }, [loadData]);

  // Auto-fetch all prices on page load (after summary is loaded)
  useEffect(() => {
    if (summary && !isLoading && !hasFetchedOnLoad.current && summary.positions.length > 0) {
      hasFetchedOnLoad.current = true;
      handleFetchAllPrices();
    }
  }, [summary, isLoading]);

  // T066: Helper function to fetch price for a single ticker (used for auto-fetch on new position)
  const fetchPriceForTicker = async (ticker: string): Promise<void> => {
    if (!summary) return;
    const homeCurrency = summary.portfolio.homeCurrency;

    try {
      // Check if this is a Euronext symbol first
      const euronextInfo = getEuronextSymbol(ticker);
      if (euronextInfo) {
        const euronextQuote = await marketDataApi.getEuronextQuote(
          euronextInfo.isin,
          euronextInfo.mic,
          homeCurrency
        );
        if (euronextQuote?.exchangeRate) {
          const syntheticQuote: StockQuoteResponse = {
            symbol: ticker,
            name: euronextQuote.name || ticker,
            price: euronextQuote.price,
            market: 4 as StockMarketType,
            source: 'Euronext',
            fetchedAt: new Date().toISOString(),
            exchangeRate: euronextQuote.exchangeRate,
            exchangeRatePair: `${euronextInfo.currency}/${homeCurrency}`,
            changePercent: euronextQuote.changePercent ?? undefined,
            change: euronextQuote.change ?? undefined,
          };
          const cacheData: CachedQuote = {
            quote: syntheticQuote,
            updatedAt: new Date().toISOString(),
            market: 4 as StockMarketType,
          };
          localStorage.setItem(getQuoteCacheKey(ticker), JSON.stringify(cacheData));
          currentPricesRef.current[ticker] = { price: euronextQuote.price, exchangeRate: euronextQuote.exchangeRate };
          await updateSummaryWithPrices({ ...currentPricesRef.current });
          setRefreshTrigger(Date.now());
        }
        return;
      }

      // Standard market handling
      const market = guessMarket(ticker);
      let quote = await stockPriceApi.getQuoteWithRate(market, ticker, homeCurrency);
      let finalMarket = market;

      if (!quote && market === StockMarket.US) {
        quote = await stockPriceApi.getQuoteWithRate(StockMarket.UK, ticker, homeCurrency);
        if (quote) finalMarket = StockMarket.UK;
      }

      if (quote?.exchangeRate) {
        const cacheData: CachedQuote = {
          quote,
          updatedAt: new Date().toISOString(),
          market: finalMarket,
        };
        localStorage.setItem(getQuoteCacheKey(ticker), JSON.stringify(cacheData));
        currentPricesRef.current[ticker] = { price: quote.price, exchangeRate: quote.exchangeRate };
        await updateSummaryWithPrices({ ...currentPricesRef.current });
        setRefreshTrigger(Date.now());
      }
    } catch (err) {
      console.error(`Failed to auto-fetch price for new position ${ticker}:`, err);
    }
  };

  const handleAddTransaction = async (data: CreateStockTransactionRequest) => {
    // T065: Capture existing tickers before save to detect new positions
    const existingTickers = new Set(summary?.positions.map(p => p.ticker) ?? []);

    if (editingTransaction) {
      await transactionApi.update(editingTransaction.id, {
        transactionDate: data.transactionDate,
        ticker: data.ticker,
        transactionType: data.transactionType,
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
    // Reload current portfolio data (not reset to first portfolio)
    if (currentPortfolioId) {
      await loadDataForPortfolio(currentPortfolioId);
    } else {
      await loadData();
    }
    setShowForm(false);
    setEditingTransaction(null);

    // T066: Auto-fetch price for new position (Buy transactions only)
    const isNewPosition = !existingTickers.has(data.ticker);
    if (isNewPosition && data.transactionType === TransactionType.Buy) {
      // Fetch price in background - don't block UI
      fetchPriceForTicker(data.ticker);
    }
  };

  const handleEditTransaction = (transaction: StockTransaction) => {
    setEditingTransaction(transaction);
    setShowForm(true);
  };

  const handleDeleteTransaction = async (transactionId: string) => {
    if (!window.confirm('確定要刪除此交易紀錄嗎？')) return;
    await transactionApi.delete(transactionId);
    // Reload current portfolio data (not reset to first portfolio)
    if (currentPortfolioId) {
      await loadDataForPortfolio(currentPortfolioId);
    } else {
      await loadData();
    }
  };

  const updateSummaryWithPrices = async (prices: Record<string, CurrentPriceInfo>) => {
    if (!portfolio || Object.keys(prices).length === 0) return;

    setIsCalculating(true);

    try {
      const [summaryData, xirrData] = await Promise.all([
        portfolioApi.getSummary(portfolio.id, prices),
        portfolioApi.calculateXirr(portfolio.id, { currentPrices: prices }),
      ]);
      setSummary(summaryData);
      setXirrResult(xirrData);

      // Cache the performance data with portfolio-specific key
      try {
        if (portfolio?.id) {
          const cacheData: CachedPerformance = {
            summary: summaryData,
            xirrResult: xirrData,
            cachedAt: new Date().toISOString(),
          };
          localStorage.setItem(getPerfCacheKey(portfolio.id), JSON.stringify(cacheData));
        }
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
  }, [portfolio]);

  const handleFetchAllPrices = async () => {
    if (!summary) return;

    setIsFetchingAll(true);

    try {
      const homeCurrency = summary.portfolio.homeCurrency;
      const fetchPromises = summary.positions.map(async (position) => {
        try {
          // Check if this is a Euronext symbol first
          const euronextInfo = getEuronextSymbol(position.ticker);
          if (euronextInfo) {
            const euronextQuote = await marketDataApi.getEuronextQuote(
              euronextInfo.isin,
              euronextInfo.mic,
              homeCurrency
            );
            if (euronextQuote?.exchangeRate) {
              // Create a compatible quote object for caching
              const syntheticQuote: StockQuoteResponse = {
                symbol: position.ticker,
                name: euronextQuote.name || position.ticker,
                price: euronextQuote.price,
                market: 4 as StockMarketType, // Euronext market type
                source: 'Euronext',
                fetchedAt: new Date().toISOString(),
                exchangeRate: euronextQuote.exchangeRate,
                exchangeRatePair: `${euronextInfo.currency}/${homeCurrency}`,
                changePercent: euronextQuote.changePercent ?? undefined,
                change: euronextQuote.change ?? undefined,
              };
              const cacheData: CachedQuote = {
                quote: syntheticQuote,
                updatedAt: new Date().toISOString(),
                market: 4 as StockMarketType,
              };
              localStorage.setItem(getQuoteCacheKey(position.ticker), JSON.stringify(cacheData));
              return { ticker: position.ticker, price: euronextQuote.price, exchangeRate: euronextQuote.exchangeRate };
            }
            return null;
          }

          // Standard market handling
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
          // Check for Euronext first in fallback
          if (isEuronextSymbol(position.ticker)) {
            console.error(`Failed to fetch Euronext price for ${position.ticker}`);
            return null;
          }

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

  const handleExportPositions = () => {
    if (!summary || summary.positions.length === 0) return;
    exportPositionsToCsv(
      summary.positions,
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
          <div className="flex items-center gap-4">
            <h1 className="text-2xl font-bold text-[var(--text-primary)]">投資組合</h1>
            <PortfolioSelector
              currentPortfolioId={currentPortfolioId}
              onPortfolioChange={(portfolioId) => {
                hasFetchedOnLoad.current = false;
                loadDataForPortfolio(portfolioId);
              }}
              onCreateNew={() => setShowCreatePortfolio(true)}
            />
          </div>
          <div className="flex items-center gap-2">
            <FileDropdown
              onExport={handleExportPositions}
              exportDisabled={summary.positions.length === 0}
            />
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

        {/* Portfolio Description (if Primary type with description) */}
        {summary.portfolio.description && (
          <p className="text-[var(--text-secondary)] text-base mb-4">{summary.portfolio.description}</p>
        )}

        {/* Create Portfolio Modal */}
        {showCreatePortfolio && (
          <CreatePortfolioForm
            onClose={() => setShowCreatePortfolio(false)}
            onSuccess={(portfolioId) => {
              setShowCreatePortfolio(false);
              hasFetchedOnLoad.current = false;
              loadDataForPortfolio(portfolioId);
            }}
          />
        )}

        {/* Performance Metrics - horizontal layout */}
        <div className="mb-6">
          <PerformanceMetrics
            summary={summary}
            xirrResult={xirrResult}
            homeCurrency={summary.portfolio.homeCurrency}
            isLoading={isCalculating}
            portfolioId={currentPortfolioId ?? undefined}
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
                portfolioId={portfolio?.id ?? ''}
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
                portfolioId={portfolio?.id ?? ''}
                initialData={editingTransaction ?? undefined}
                onSubmit={handleAddTransaction}
                onCancel={() => {
                  setShowForm(false);
                  setEditingTransaction(null);
                }}
                isForeignCurrencyPortfolio={portfolio?.portfolioType === PortfolioType.ForeignCurrency}
              />
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
