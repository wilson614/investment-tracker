import { useState, useEffect, useCallback, useRef } from 'react';
import { useParams, Link } from 'react-router-dom';
import { Pencil, RefreshCw, Loader2, Download } from 'lucide-react';
import { portfolioApi, stockPriceApi } from '../services/api';
import { TransactionForm } from '../components/transactions/TransactionForm';
import { TransactionList } from '../components/transactions/TransactionList';
import { PositionCard } from '../components/portfolio/PositionCard';
import { PerformanceMetrics } from '../components/portfolio/PerformanceMetrics';
import { StockImportButton } from '../components/import';
import { exportTransactionsToCsv, exportPositionsToCsv } from '../services/csvExport';
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
  const { id } = useParams<{ id: string }>();

  // Load cached performance data on init (no time limit - always show cached, then refresh)
  const loadCachedPerformance = (): { summary: PortfolioSummary | null; xirrResult: XirrResult | null } => {
    if (!id) return { summary: null, xirrResult: null };
    try {
      const cached = localStorage.getItem(getPerformanceCacheKey(id));
      if (cached) {
        const data: CachedPerformance = JSON.parse(cached);
        return { summary: data.summary, xirrResult: data.xirrResult };
      }
    } catch {
      // Ignore cache errors
    }
    return { summary: null, xirrResult: null };
  };

  const cachedPerf = loadCachedPerformance();
  const [summary, setSummary] = useState<PortfolioSummary | null>(cachedPerf.summary);
  const [xirrResult, setXirrResult] = useState<XirrResult | null>(cachedPerf.xirrResult);
  const [isLoading, setIsLoading] = useState(cachedPerf.summary === null);
  const [isCalculating, setIsCalculating] = useState(false);
  const [isFetchingAll, setIsFetchingAll] = useState(false);
  const [refreshTrigger, setRefreshTrigger] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);
  const [editingTransaction, setEditingTransaction] = useState<StockTransaction | null>(null);
  const [transactions, setTransactions] = useState<StockTransaction[]>([]);
  const [isEditingDescription, setIsEditingDescription] = useState(false);
  const [editDescription, setEditDescription] = useState('');

  const currentPricesRef = useRef<Record<string, CurrentPriceInfo>>({});

  const loadData = useCallback(async () => {
    if (!id) return;

    try {
      setIsLoading(true);
      setError(null);
      const [basicSummary, txData] = await Promise.all([
        portfolioApi.getSummary(id),
        transactionApi.getByPortfolio(id),
      ]);
      setTransactions(txData);

      // Load cached prices for all positions
      const tickers = basicSummary.positions.map(pos => pos.ticker);
      const cachedPrices = loadCachedPrices(tickers);
      currentPricesRef.current = cachedPrices;

      // If we have cached prices, calculate with them immediately
      if (Object.keys(cachedPrices).length > 0) {
        const [summaryWithPrices, xirr] = await Promise.all([
          portfolioApi.getSummary(id, cachedPrices),
          portfolioApi.calculateXirr(id, { currentPrices: cachedPrices }),
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
  }, [id]);

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

  const handleAddTransaction = async (data: CreateStockTransactionRequest) => {
    if (editingTransaction) {
      await transactionApi.update(editingTransaction.id, {
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
    await loadData();
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
    await loadData();
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

      // Cache the performance data
      try {
        const cacheData: CachedPerformance = {
          summary: summaryData,
          xirrResult: xirrData,
          cachedAt: new Date().toISOString(),
        };
        localStorage.setItem(getPerformanceCacheKey(id), JSON.stringify(cacheData));
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

  const handleStartEditDescription = () => {
    if (!summary) return;
    setEditDescription(summary.portfolio.description ?? '');
    setIsEditingDescription(true);
  };

  const handleSaveDescription = async () => {
    if (!summary) return;
    try {
      await portfolioApi.update(summary.portfolio.id, {
        description: editDescription.trim() || undefined,
      });
      setIsEditingDescription(false);
      await loadData();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update');
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
          <div>
            {isEditingDescription ? (
              <div className="space-y-3">
                <input
                  type="text"
                  value={editDescription}
                  onChange={(e) => setEditDescription(e.target.value)}
                  className="input-dark w-full"
                  autoFocus
                  placeholder="描述（選填）"
                />
                <div className="flex gap-2">
                  <button onClick={handleSaveDescription} className="btn-accent py-1 px-4">儲存</button>
                  <button onClick={() => setIsEditingDescription(false)} className="btn-dark py-1 px-4">取消</button>
                </div>
              </div>
            ) : (
              <>
                <div className="flex items-center gap-2">
                  <h1 className="text-2xl font-bold text-[var(--text-primary)]">投資組合</h1>
                  <button
                    onClick={handleStartEditDescription}
                    className="p-1 text-[var(--text-muted)] hover:text-[var(--accent-butter)] hover:bg-[var(--bg-hover)] rounded transition-colors"
                    title="編輯描述"
                  >
                    <Pencil className="w-4 h-4" />
                  </button>
                </div>
                {summary.portfolio.description && (
                  <p className="text-[var(--text-secondary)] text-base mt-1">{summary.portfolio.description}</p>
                )}
              </>
            )}
          </div>
          {!isEditingDescription && (
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={handleExportPositions}
                disabled={summary.positions.length === 0}
                className="btn-dark flex items-center gap-2 px-3 py-1.5 text-sm disabled:opacity-50"
                title="匯出持倉明細"
              >
                <Download className="w-4 h-4" />
                匯出
              </button>
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
            <h2 className="text-lg font-bold text-[var(--text-primary)] mb-4">
              持倉 <span className="text-sm font-normal text-[var(--text-muted)]">（點擊查看詳情）</span>
            </h2>
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
              <button
                onClick={handleExportTransactions}
                disabled={transactions.length === 0}
                className="btn-dark flex items-center gap-2 px-3 py-1.5 text-sm disabled:opacity-50"
                title="匯出交易"
              >
                <Download className="w-4 h-4" />
                匯出
              </button>
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
                portfolioId={id!}
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
