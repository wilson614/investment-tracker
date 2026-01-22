/**
 * Portfolio Page
 *
 * 投資組合主要操作頁：顯示持倉、交易清單、績效摘要，並提供新增/編輯/刪除交易與匯入/匯出功能。
 *
 * 特色：
 * - 先讀取 localStorage 的報價快取，讓績效與持倉能更快顯示。
 * - 在頁面載入後自動更新所有報價，並在新增持倉時針對單一 ticker 觸發自動抓價。
 * - 支援 Euronext 報價流程（透過 `marketDataApi.getEuronextQuote` 產生 synthetic quote）。
 */
import { useState, useEffect, useCallback, useRef } from 'react';
import { Link } from 'react-router-dom';
import { RefreshCw, Loader2 } from 'lucide-react';
import { portfolioApi, stockPriceApi, marketDataApi } from '../services/api';
import { TransactionForm } from '../components/transactions/TransactionForm';
import { TransactionList } from '../components/transactions/TransactionList';
import { PositionCard } from '../components/portfolio/PositionCard';
import { PerformanceMetrics } from '../components/portfolio/PerformanceMetrics';
// Single portfolio mode (FR-080): PortfolioSelector and CreatePortfolioForm are hidden
// import { PortfolioSelector } from '../components/portfolio/PortfolioSelector';
// import { CreatePortfolioForm } from '../components/portfolio/CreatePortfolioForm';
import { StockImportButton } from '../components/import';
import { FileDropdown } from '../components/common';
import { exportTransactionsToCsv } from '../services/csvExport';
import { usePortfolio } from '../contexts/PortfolioContext';
import { StockMarket, TransactionType } from '../types';
import type { Portfolio, PortfolioSummary, CreateStockTransactionRequest, XirrResult, CurrentPriceInfo, StockMarket as StockMarketType, StockTransaction, StockQuoteResponse } from '../types';
import { transactionApi } from '../services/api';

/**
 * 依 ticker 格式推測市場別。
 *
 * - TW：純數字或數字+英文字尾（例如 `2330`、`00878`、`6547M`）
 * - UK：以 `.L` 結尾（London Stock Exchange）
 * - 其他：預設 US
 */
const guessMarket = (ticker: string): StockMarketType => {
  if (/^\d+[A-Za-z]*$/.test(ticker)) {
    return StockMarket.TW;
  }
  if (ticker.endsWith('.L')) {
    return StockMarket.UK;
  }
  return StockMarket.US;
};

/**
 * localStorage 快取 key。
 *
 * 注意：perf cache 會包含 portfolioId，用來避免跨帳號/跨投資組合讀到錯誤資料。
 */
const getPerfCacheKey = (portfolioId: string) => `perf_cache_${portfolioId}`;
const getQuoteCacheKey = (ticker: string, market?: StockMarketType) =>
  `quote_cache_${ticker}_${market ?? 'default'}`;

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

/**
 * 依 ticker 格式推測市場別。
 */
const guessMarketForCache = (ticker: string): StockMarketType => {
  if (/^\d+[A-Za-z]*$/.test(ticker)) {
    return StockMarket.TW;
  }
  if (ticker.endsWith('.L')) {
    return StockMarket.UK;
  }
  return StockMarket.US;
};

/**
 * 從 localStorage 載入報價快取（依 ticker + market）。
 *
 * 設計重點：
 * - 不限制快取時效：進頁面時先用快取快速 render。
 * - 僅在快取包含 exchangeRate 時回填，避免本位幣換算資料不完整。
 */
const loadCachedPrices = (positions: { ticker: string; market?: StockMarketType }[]): Record<string, CurrentPriceInfo> => {
  const prices: Record<string, CurrentPriceInfo> = {};

  for (const pos of positions) {
    try {
      const market = pos.market ?? guessMarketForCache(pos.ticker);
      const cached = localStorage.getItem(getQuoteCacheKey(pos.ticker, market));
      if (cached) {
        const data: CachedQuote = JSON.parse(cached);
        if (data.quote.exchangeRate) {
          prices[pos.ticker] = {
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
  // Use shared portfolio context for cross-page synchronization
  // Single portfolio mode (FR-080): refreshPortfolios removed as multi-portfolio UI is hidden
  const { currentPortfolioId, selectPortfolio, clearPerformanceState } = usePortfolio();
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
  // Single portfolio mode (FR-080): showCreatePortfolio removed
  const [editingTransaction, setEditingTransaction] = useState<StockTransaction | null>(null);
  const [transactions, setTransactions] = useState<StockTransaction[]>([]);

  const currentPricesRef = useRef<Record<string, CurrentPriceInfo>>({});
  const importTriggerRef = useRef<(() => void) | null>(null);

  /**
   * 載入指定投資組合的資料（summary、交易清單），並嘗試套用報價快取做快速計算。
   *
   * 這個方法會先清空舊資料，避免 UI 短暫顯示上一個 portfolio 的內容（FR-100）。
   */
  const loadDataForPortfolio = useCallback(async (portfolioId: string) => {
    try {
      setIsLoading(true);
      setError(null);
      // Clear stale data immediately to prevent showing previous portfolio's data (FR-100)
      setSummary(null);
      setXirrResult(null);
      setTransactions([]);
      // Also clear performance state in context (for Performance page)
      clearPerformanceState();

      const currentPortfolio = await portfolioApi.getById(portfolioId);
      setPortfolio(currentPortfolio);

      const [basicSummary, txData] = await Promise.all([
        portfolioApi.getSummary(portfolioId),
        transactionApi.getByPortfolio(portfolioId),
      ]);
      setTransactions(txData);

      // Load cached prices for all positions (using position with market info)
      const cachedPrices = loadCachedPrices(basicSummary.positions);
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
  }, [clearPerformanceState]);

  /**
   * 載入目前使用者的投資組合（若不存在則建立預設投資組合），並取得 summary / 交易。
   *
   * 單一投資組合模式：只使用第一個 portfolio，並透過 `selectPortfolio` 同步到全域 context。
   */
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

      selectPortfolio(currentPortfolio.id);

      setPortfolio(currentPortfolio);
      const portfolioId = currentPortfolio.id;

      const [basicSummary, txData] = await Promise.all([
        portfolioApi.getSummary(portfolioId),
        transactionApi.getByPortfolio(portfolioId),
      ]);
      setTransactions(txData);

      // Load cached prices for all positions (using position with market info)
      const cachedPrices = loadCachedPrices(basicSummary.positions);
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
  }, [selectPortfolio]);

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

  /**
   * 取得單一 ticker 的報價（含匯率），並用最新 prices 更新 summary。
   *
   * 使用時機：新增持倉後，自動補上新標的的報價，讓持倉/績效能立即更新。
   */
  const fetchPriceForTicker = async (ticker: string, market?: StockMarketType): Promise<void> => {
    if (!summary) return;
    const homeCurrency = summary.portfolio.homeCurrency;

    try {
      // Euronext 標的透過 ticker 查詢（後端會自動解析 ISIN/MIC）
      if (market === 4) {
        const euronextQuote = await marketDataApi.getEuronextQuoteByTicker(ticker, homeCurrency);
        if (euronextQuote?.exchangeRate) {
          const syntheticQuote: StockQuoteResponse = {
            symbol: ticker,
            name: euronextQuote.name || ticker,
            price: euronextQuote.price,
            market: 4 as StockMarketType,
            source: 'Euronext',
            fetchedAt: new Date().toISOString(),
            exchangeRate: euronextQuote.exchangeRate,
            exchangeRatePair: `${euronextQuote.currency}/${homeCurrency}`,
            changePercent: euronextQuote.changePercent ?? undefined,
            change: euronextQuote.change ?? undefined,
          };
          const cacheData: CachedQuote = {
            quote: syntheticQuote,
            updatedAt: new Date().toISOString(),
            market: 4 as StockMarketType,
          };
          localStorage.setItem(getQuoteCacheKey(ticker, 4 as StockMarketType), JSON.stringify(cacheData));
          currentPricesRef.current[ticker] = { price: euronextQuote.price, exchangeRate: euronextQuote.exchangeRate };
          await updateSummaryWithPrices({ ...currentPricesRef.current });
          setRefreshTrigger(Date.now());
        }
        return;
      }

      // Standard market handling - no fallback since market is explicitly specified
      const targetMarket = market ?? guessMarket(ticker);
      const quote = await stockPriceApi.getQuoteWithRate(targetMarket, ticker, homeCurrency);

      if (quote?.exchangeRate) {
        const cacheData: CachedQuote = {
          quote,
          updatedAt: new Date().toISOString(),
          market: targetMarket,
        };
        localStorage.setItem(getQuoteCacheKey(ticker, targetMarket), JSON.stringify(cacheData));
        currentPricesRef.current[ticker] = { price: quote.price, exchangeRate: quote.exchangeRate };
        await updateSummaryWithPrices({ ...currentPricesRef.current });
        setRefreshTrigger(Date.now());
      }
    } catch (err) {
      console.error(`Failed to auto-fetch price for new position ${ticker}:`, err);
    }
  };

  /**
   * 新增/更新交易後，重新載入資料。
   *
   * 行內重點：
   * - 先記錄既有 tickers，用來判斷這筆買入是否產生「新持倉」（T066）。
   * - 若是新持倉買入，背景觸發單一 ticker 抓價，不阻塞 UI。
   */
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
        exchangeRate: data.exchangeRate,
        fees: data.fees,
        fundSource: data.fundSource,
        currencyLedgerId: data.currencyLedgerId,
        notes: data.notes,
        market: data.market,
        currency: data.currency,
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
      fetchPriceForTicker(data.ticker, data.market);
    }
  };

  /**
   * 進入編輯模式：把目標交易帶入表單。
   */
  const handleEditTransaction = (transaction: StockTransaction) => {
    setEditingTransaction(transaction);
    setShowForm(true);
  };

  /**
   * 刪除單筆交易後重新載入資料。
   * @param transactionId 交易 ID
   */
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

  /**
   * 以給定的最新 prices 重新計算 summary 與 XIRR，並同步寫入 localStorage 快取。
   *
   * 注意：如果 prices 為空，或還沒有 portfolio 物件，則不會進行任何計算。
   */
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

  /**
   * 由子元件（例如 PositionCard）回傳單一持倉的最新價格/匯率時，用來更新整體 summary。
   */
  const handlePositionPriceUpdate = useCallback((ticker: string, price: number, exchangeRate: number) => {
    currentPricesRef.current[ticker] = { price, exchangeRate };
    updateSummaryWithPrices({ ...currentPricesRef.current });
  }, [portfolio]);

  /**
   * 抓取所有持倉的最新報價（含匯率），並更新 summary / XIRR。
   *
   * 補充：
   * - 先判斷是否為 Euronext symbol（market === 4），若是則走 Euronext quote 流程。
   * - 其餘走一般市場（TW/US/UK）報價，US 失敗時嘗試 UK。
   */
  const handleFetchAllPrices = async () => {
    if (!summary) return;

    setIsFetchingAll(true);

    try {
      const homeCurrency = summary.portfolio.homeCurrency;
      const fetchPromises = summary.positions.map(async (position) => {
        try {
          // Use Euronext API if position's market is EU (4)
          const positionMarket = position.market ?? guessMarket(position.ticker);
          if (positionMarket === 4) {
            const euronextQuote = await marketDataApi.getEuronextQuoteByTicker(
              position.ticker,
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
                exchangeRatePair: `${euronextQuote.currency}/${homeCurrency}`,
                changePercent: euronextQuote.changePercent ?? undefined,
                change: euronextQuote.change ?? undefined,
              };
              const cacheData: CachedQuote = {
                quote: syntheticQuote,
                updatedAt: new Date().toISOString(),
                market: 4 as StockMarketType,
              };
              localStorage.setItem(getQuoteCacheKey(position.ticker, 4 as StockMarketType), JSON.stringify(cacheData));
              return { ticker: position.ticker, price: euronextQuote.price, exchangeRate: euronextQuote.exchangeRate };
            }
            return null;
          }

          // Standard market handling - no fallback since market is explicitly specified
          const market = position.market ?? guessMarket(position.ticker);
          const quote = await stockPriceApi.getQuoteWithRate(market, position.ticker, homeCurrency);

          if (quote?.exchangeRate) {
            // Save full quote to cache for PositionCard to use
            const cacheData: CachedQuote = {
              quote,
              updatedAt: new Date().toISOString(),
              market,
            };
            localStorage.setItem(getQuoteCacheKey(position.ticker, market), JSON.stringify(cacheData));
            return { ticker: position.ticker, price: quote.price, exchangeRate: quote.exchangeRate };
          }
          return null;
        } catch {
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

  /**
   * 匯出交易清單為 CSV。
   */
  const handleExportTransactions = () => {
    if (!summary || transactions.length === 0) return;
    exportTransactionsToCsv(
      transactions,
      summary.portfolio.baseCurrency,
      summary.portfolio.homeCurrency
    );
  };

  // FR-130: handleExportPositions removed - Export Positions feature removed from UI

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
            {/* Single portfolio mode (FR-080): PortfolioSelector hidden */}
          </div>
          <div className="flex items-center gap-2">
            {/* FR-130: Export Positions button removed - only Export Transactions remains in transaction history section */}
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

        {/* Single portfolio mode (FR-080): CreatePortfolioForm hidden */}

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
                  key={`${position.ticker}-${position.market ?? 'default'}`}
                  to={`/portfolio/position/${position.ticker}${position.market != null ? `/${position.market}` : ''}`}
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
              />
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
