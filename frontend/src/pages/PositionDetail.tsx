/**
 * Position Detail Page
 *
 * 單一持倉詳情頁：顯示該 ticker 的持倉資訊、交易明細、即時報價與單一持倉 XIRR。
 *
 * 特色：
 * - 進頁面先套用 localStorage 快取 quote / XIRR 讓數字立即可見，再自動抓最新報價。
 * - US14: 報價嚴格依照 position 的 market 獲取，不再有 US→UK fallback。
 */
import { useState, useEffect, useCallback, useRef } from 'react';
import { useParams, Link } from 'react-router-dom';
import { ArrowLeft, RefreshCw, Loader2 } from 'lucide-react';
import { portfolioApi, transactionApi, stockPriceApi } from '../services/api';
import { Skeleton } from '../components/common/SkeletonLoader';
import { exportTransactionsToCsv } from '../services/csvExport';
import { TransactionList } from '../components/transactions/TransactionList';
import { ConfirmationModal } from '../components/modals/ConfirmationModal';
import { FileDropdown } from '../components/common';
import { usePortfolio } from '../contexts/PortfolioContext';
import { StockMarket } from '../types';
import type {
  Portfolio,
  StockPosition,
  StockTransaction,
  StockMarket as StockMarketType,
  StockQuoteResponse,
  XirrResult,
} from '../types';

/**
 * 依 ticker 格式推測市場別（TW/UK/US）。
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

const MARKET_LABELS: Record<StockMarketType, string> = {
  [StockMarket.TW]: '台股',
  [StockMarket.US]: '美股',
  [StockMarket.UK]: '英股',
  [StockMarket.EU]: '歐股',
};

/**
 * localStorage 快取 key。
 * - quote cache：與 PositionCard/Portfolio/Dashboard 共用
 * - xirr cache：以 portfolioId + ticker 區隔，避免跨投資組合汙染
 */
const getQuoteCacheKey = (ticker: string, market?: StockMarketType) =>
  `quote_cache_${ticker}_${market ?? 'default'}`;
const getLegacyQuoteCacheKey = (ticker: string) => `quote_cache_${ticker}`;
const getXirrCacheKey = (portfolioId: string, ticker: string) => `xirr_cache_${portfolioId}_${ticker}`;

interface CachedQuote {
  quote: StockQuoteResponse;
  updatedAt: string;
  market: StockMarketType;
}

interface CachedXirr {
  xirr: XirrResult;
  cachedAt: string;
}

/**
 * 從 localStorage 載入報價快取。
 *
 * 回傳 `market` 會以快取為優先；若無快取則用 `guessMarket` 推測。
 */
const loadCachedQuote = (
  ticker: string,
  market?: StockMarketType,
): { quote: StockQuoteResponse | null; updatedAt: Date | null; market: StockMarketType } => {
  const resolvedMarket = market ?? guessMarket(ticker);

  try {
    const marketAwareCached = localStorage.getItem(getQuoteCacheKey(ticker, resolvedMarket));
    if (marketAwareCached) {
      const data: CachedQuote = JSON.parse(marketAwareCached);
      return {
        quote: data.quote,
        updatedAt: new Date(data.updatedAt),
        market: data.market,
      };
    }

    const legacyCached = localStorage.getItem(getLegacyQuoteCacheKey(ticker));
    if (legacyCached) {
      const data: CachedQuote = JSON.parse(legacyCached);
      return {
        quote: data.quote,
        updatedAt: new Date(data.updatedAt),
        market: data.market,
      };
    }
  } catch {
    // Ignore cache errors
  }

  return { quote: null, updatedAt: null, market: resolvedMarket };
};

/**
 * 從 localStorage 載入單一持倉 XIRR 快取。
 *
 * 設計：不限制快取時效，先讓 UI 有數字，再在頁面載入後背景刷新。
 */
const loadCachedXirr = (portfolioId: string, ticker: string): XirrResult | null => {
  try {
    const cached = localStorage.getItem(getXirrCacheKey(portfolioId, ticker));
    if (cached) {
      const data: CachedXirr = JSON.parse(cached);
      return data.xirr;
    }
  } catch {
    // Ignore cache errors
  }
  return null;
};

export function PositionDetailPage() {
  const { currentPortfolioId, invalidateSharedCaches } = usePortfolio();

  const { ticker, market: marketParam } = useParams<{ ticker: string; market?: string }>();
  // Parse market from URL param (if provided)
  const urlMarket: StockMarketType | undefined = marketParam ? parseInt(marketParam, 10) as StockMarketType : undefined;
  const [portfolio, setPortfolio] = useState<Portfolio | null>(null);
  const [portfolioId, setPortfolioId] = useState<string | null>(null);
  const [position, setPosition] = useState<StockPosition | null>(null);
  const [transactions, setTransactions] = useState<StockTransaction[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const scrollYRef = useRef<number>(0);

  // Modal state
  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [deletingTransactionId, setDeletingTransactionId] = useState<string | null>(null);

  // Load cached quote on init (use useRef to avoid re-creating on every render)
  const cachedDataRef = useRef(
    ticker ? loadCachedQuote(ticker, urlMarket) : { quote: null, updatedAt: null, market: StockMarket.US as StockMarketType },
  );
  const cachedData = cachedDataRef.current;
  // XIRR cache will be loaded after we have portfolioId
  const cachedXirrRef = useRef<XirrResult | null>(null);

  // Quote state - initialize from cache, but prefer URL market if provided
  const initialMarket = urlMarket ?? cachedData.market;
  const [selectedMarket] = useState<StockMarketType>(initialMarket);
  const [fetchStatus, setFetchStatus] = useState<'idle' | 'loading' | 'success' | 'error'>(
    cachedData.quote ? 'success' : 'idle'
  );
  const [lastQuote, setLastQuote] = useState<StockQuoteResponse | null>(cachedData.quote);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(cachedData.updatedAt);
  const [positionXirr, setPositionXirr] = useState<XirrResult | null>(cachedXirrRef.current);

  // Track if we have data loaded (to detect refresh vs initial)
  const isDataLoadedRef = useRef(false);
  if (position) isDataLoadedRef.current = true;

  // Auto-fetch tracking
  const hasFetched = useRef(false);
  const hasAppliedCache = useRef(false);

  /**
   * 載入持倉頁面所需資料：portfolio、指定 ticker 的 position、以及該 ticker 的交易明細。
   *
   * 若 quote cache 存在，會先用快取價格計算 currentValue / PnL，讓頁面能更快顯示。
   */
  const loadData = useCallback(async () => {
    if (!ticker) return;

    try {
      // 只有在已經有資料的情況下（例如刪除交易後重整），才需要記住 scroll 位置
      if (isDataLoadedRef.current) {
        scrollYRef.current = window.scrollY;
      } else {
        scrollYRef.current = 0;
        setIsLoading(true);
      }
      setError(null);

      if (!currentPortfolioId) {
        setError('找不到投資組合');
        setPortfolio(null);
        setPosition(null);
        setTransactions([]);
        return;
      }
      setPortfolioId(currentPortfolioId);

      // Load cached XIRR now that we have portfolioId
      if (!cachedXirrRef.current) {
        cachedXirrRef.current = loadCachedXirr(currentPortfolioId, ticker);
        if (cachedXirrRef.current) {
          setPositionXirr(cachedXirrRef.current);
        }
      }

      const [summaryData, txData] = await Promise.all([
        portfolioApi.getSummary(currentPortfolioId),
        transactionApi.getByPortfolio(currentPortfolioId),
      ]);

      setPortfolio(summaryData.portfolio);

      // Find position for this ticker (and market if specified in URL)
      // When urlMarket is provided, we match exactly; otherwise match by ticker only
      const pos = summaryData.positions.find((p) => {
        const tickerMatch = p.ticker.toUpperCase() === ticker.toUpperCase();
        if (urlMarket != null) {
          return tickerMatch && p.market === urlMarket;
        }
        return tickerMatch;
      });

      // If we have cached quote, apply it immediately to position
      if (pos && cachedData.quote && cachedData.quote.exchangeRate) {
        const currentValue = pos.totalShares * cachedData.quote.price * cachedData.quote.exchangeRate;
        const totalCostHome = pos.totalCostHome ?? 0;
        const pnl = currentValue - totalCostHome;
        const pnlPct = totalCostHome > 0 ? (pnl / totalCostHome) * 100 : 0;

        const currentValueSource = pos.totalShares * cachedData.quote.price;
        const totalCostSource = pos.totalCostSource;
        const pnlSource = currentValueSource - totalCostSource;
        const pnlPctSource = totalCostSource > 0 ? (pnlSource / totalCostSource) * 100 : 0;

        setPosition({
          ...pos,
          currentPrice: cachedData.quote.price,
          currentExchangeRate: cachedData.quote.exchangeRate,
          currentValueHome: currentValue,
          unrealizedPnlHome: pnl,
          unrealizedPnlPercentage: pnlPct,
          currentValueSource,
          unrealizedPnlSource: pnlSource,
          unrealizedPnlSourcePercentage: pnlPctSource,
        });
        hasAppliedCache.current = true;
      } else {
        setPosition(pos || null);
      }

      // Filter transactions for this ticker (and market if specified in URL)
      const tickerTx = txData.filter((t) => {
        const tickerMatch = t.ticker.toUpperCase() === ticker.toUpperCase();
        if (urlMarket != null) {
          return tickerMatch && t.market === urlMarket;
        }
        return tickerMatch;
      });
      setTransactions(tickerTx);
    } catch (err) {
      setError(err instanceof Error ? err.message : '載入失敗');
    } finally {
      setIsLoading(false);
      // Restore scroll position only if we have a stored position > 0 (refresh case)
      if (scrollYRef.current > 0) {
        requestAnimationFrame(() => {
          window.scrollTo({ top: scrollYRef.current });
        });
      }
    }
  }, [ticker, urlMarket, currentPortfolioId]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  // Auto-fetch quote on mount (always fetch fresh on page load)
  useEffect(() => {
    if (!hasFetched.current && portfolio && portfolioId && position && !isLoading) {
      hasFetched.current = true;
      // Always fetch fresh quote on page load
      handleFetchQuote();

      // If we have cached quote and haven't calculated XIRR yet (and no cached XIRR), do it now
      if (hasAppliedCache.current && lastQuote && !cachedXirrRef.current) {
        portfolioApi.calculatePositionXirr(
          portfolioId,
          ticker!,
          {
            currentPrice: lastQuote.price,
            currentExchangeRate: lastQuote.exchangeRate,
          }
        ).then((result) => {
          setPositionXirr(result);
          // Save to cache
          try {
            localStorage.setItem(getXirrCacheKey(portfolioId, ticker!), JSON.stringify({
              xirr: result,
              cachedAt: new Date().toISOString(),
            }));
          } catch {
            // Ignore cache errors
          }
        }).catch(() => {
          // 保持既有值，避免 UI 閃爍成 '-'
        });
      }
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [portfolio, portfolioId, position, isLoading]);

  /**
   * 取得最新報價（含匯率）並更新：
   * - quote state（lastQuote/lastUpdated/fetchStatus）
   * - localStorage quote cache
   * - position 的 currentValue / PnL
   *
   * US14: 不再有 US→UK fallback，嚴格使用 position 的 market。
   */
  const handleFetchQuote = async () => {
    if (!portfolio || !portfolioId) return;

    setFetchStatus('loading');

    try {
      const quote = await stockPriceApi.getQuoteWithRate(
        selectedMarket,
        ticker!,
        portfolio.homeCurrency
      );

      // US14: No market fallback - strictly use position's market
      if (quote) {
        const now = new Date();
        setLastQuote(quote);
        setLastUpdated(now);
        setFetchStatus('success');

        // Save to localStorage cache (same format as PositionCard)
        try {
          localStorage.setItem(getQuoteCacheKey(ticker!, selectedMarket), JSON.stringify({
            quote,
            updatedAt: now.toISOString(),
            market: quote.market,
          }));
        } catch {
          // Ignore cache errors
        }

        // Recalculate position with current price
        if (position && quote.exchangeRate) {
          const currentValue = position.totalShares * quote.price * quote.exchangeRate;
          const totalCostHome = position.totalCostHome ?? 0;
          const pnl = currentValue - totalCostHome;
          const pnlPct = totalCostHome > 0 ? (pnl / totalCostHome) * 100 : 0;

          const currentValueSource = position.totalShares * quote.price;
          const totalCostSource = position.totalCostSource;
          const pnlSource = currentValueSource - totalCostSource;
          const pnlPctSource = totalCostSource > 0 ? (pnlSource / totalCostSource) * 100 : 0;

          setPosition({
            ...position,
            currentPrice: quote.price,
            currentExchangeRate: quote.exchangeRate,
            currentValueHome: currentValue,
            unrealizedPnlHome: pnl,
            unrealizedPnlPercentage: pnlPct,
            currentValueSource,
            unrealizedPnlSource: pnlSource,
            unrealizedPnlSourcePercentage: pnlPctSource,
          });

          // Calculate XIRR for this position
          try {
            const xirrResult = await portfolioApi.calculatePositionXirr(
              portfolioId,
              ticker!,
              {
                currentPrice: quote.price,
                currentExchangeRate: quote.exchangeRate,
              }
            );
            setPositionXirr(xirrResult);
            // Save to cache
            try {
              localStorage.setItem(getXirrCacheKey(portfolioId, ticker!), JSON.stringify({
                xirr: xirrResult,
                cachedAt: new Date().toISOString(),
              }));
            } catch {
              // Ignore cache errors
            }
          } catch {
            // XIRR calculation failed - keep previous value to avoid flicker
          }
        }
      } else {
        setFetchStatus('error');
      }
    } catch {
      setFetchStatus('error');
    }
  };

  /**
   * 觸發刪除確認對話框
   */
  const handleDeleteClick = (transactionId: string) => {
    setDeletingTransactionId(transactionId);
    setShowDeleteModal(true);
  };

  /**
   * 執行刪除
   */
  const handleConfirmDelete = async () => {
    if (!deletingTransactionId) return;

    await transactionApi.delete(deletingTransactionId);
    invalidateSharedCaches();
    await loadData();
    setDeletingTransactionId(null);
  };

  /**
   * 匯出該 ticker 的交易明細為 CSV。
   */
  const handleExportTransactions = () => {
    if (!portfolio || transactions.length === 0) return;
    exportTransactionsToCsv(
      transactions,
      portfolio.baseCurrency,
      portfolio.homeCurrency,
      `${ticker}_交易紀錄_${new Date().toISOString().split('T')[0]}.csv`
    );
  };

  const formatNumber = (value: number | null | undefined, decimals = 2) => {
    if (value == null) return '-';
    return value.toLocaleString('zh-TW', {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    });
  };

  const formatSignedNumber = (value: number | null | undefined, decimals = 2) => {
    if (value == null) return '-';
    const sign = value >= 0 ? '+' : '';
    return `${sign}${formatNumber(value, decimals)}`;
  };

  const formatTWD = (value: number | null | undefined) => {
    if (value == null) return '-';
    return Math.round(value).toLocaleString('zh-TW');
  };

  const formatPercent = (value: number | null | undefined) => {
    if (value == null) return '-';
    const sign = value >= 0 ? '+' : '';
    return `${sign}${formatNumber(value, 2)}%`;
  };

  const formatTime = (date: Date) => {
    return date.toLocaleTimeString('zh-TW', { hour: '2-digit', minute: '2-digit' });
  };

  if (isLoading && !position) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-[var(--text-muted)] text-lg">載入中...</div>
      </div>
    );
  }

  if (error || !portfolio) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-[var(--color-danger)] text-lg">{error || '找不到資料'}</div>
      </div>
    );
  }

  const pnlColor =
    (position?.unrealizedPnlHome ?? 0) >= 0 ? 'number-positive' : 'number-negative';
  const homeCurrency = portfolio.homeCurrency;
  const baseCurrency = portfolio.baseCurrency;

  return (
    <div className="min-h-screen py-8">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        {/* Back button */}
        <Link
          to="/portfolio"
          className="flex items-center gap-2 text-[var(--text-secondary)] hover:text-[var(--text-primary)] mb-6 text-base transition-colors"
        >
          <ArrowLeft className="w-5 h-5" />
          返回投資組合
        </Link>

        {/* Header */}
        <div className="flex justify-between items-start mb-6">
          <div>
            <div className="flex items-center gap-3">
              <h1 className="text-2xl font-bold text-[var(--accent-cream)]">{ticker}</h1>
              <span className="text-sm text-[var(--text-muted)] bg-[var(--bg-tertiary)] px-2 py-0.5 rounded">
                {MARKET_LABELS[selectedMarket]}
              </span>
            </div>
          </div>
          <div className="flex items-center gap-3">
            <button
              onClick={handleFetchQuote}
              disabled={fetchStatus === 'loading'}
              className="btn-dark flex items-center gap-2 disabled:opacity-50"
            >
              {fetchStatus === 'loading' ? (
                <Loader2 className="w-4 h-4 animate-spin" />
              ) : (
                <RefreshCw className="w-4 h-4" />
              )}
              獲取報價
            </button>
            {fetchStatus === 'error' && (
              <span className="text-sm text-[var(--color-danger)]">
                無法取得報價
              </span>
            )}
          </div>
        </div>

        {/* Position Metrics */}
        {position ? (
          <div className="card-dark p-6 mb-6">
            <div className="flex justify-between items-center mb-4">
              <h2 className="text-lg font-bold text-[var(--text-primary)]">持倉概況</h2>
              {lastUpdated && (
                <span className="text-sm text-[var(--text-muted)]">
                  報價更新於 {formatTime(lastUpdated)}
                </span>
              )}
            </div>

            <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
              <div className="metric-card">
                <p className="text-sm text-[var(--text-muted)] mb-1">持股數量</p>
                <p className="text-xl font-bold text-[var(--text-primary)] number-display">
                  {formatNumber(position.totalShares, 4)}
                </p>
                <p className="text-sm text-[var(--text-muted)]">股</p>
              </div>

              <div className="metric-card">
                <p className="text-sm text-[var(--text-muted)] mb-1">單位成本</p>
                <p className="text-xl font-bold text-[var(--text-primary)] number-display">
                  {formatNumber(position.averageCostPerShareSource)}
                </p>
                <p className="text-sm text-[var(--text-muted)]">{baseCurrency}/股</p>
              </div>

              <div className="metric-card">
                <p className="text-sm text-[var(--text-muted)] mb-1">總成本</p>
                <p className="text-xl font-bold text-[var(--text-primary)] number-display">
                  {formatTWD(position.totalCostHome)}
                </p>
                <p className="text-sm text-[var(--text-muted)]">{homeCurrency}</p>
              </div>

              <div className="metric-card">
                <p className="text-sm text-[var(--text-muted)] mb-1">現價</p>
                {lastQuote ? (
                  <>
                    <p className="text-xl font-bold text-[var(--text-primary)] number-display">
                      {formatNumber(lastQuote.price)}
                    </p>
                    <p className="text-sm text-[var(--text-muted)]">
                      {`${baseCurrency} · 匯率 ${formatNumber(lastQuote.exchangeRate ?? 0, 4)}`}
                    </p>
                  </>
                ) : (
                  <>
                    <Skeleton width="w-24" height="h-7" />
                    <Skeleton width="w-32" height="h-5" />
                  </>
                )}
              </div>
            </div>

            <hr className="border-[var(--border-color)] my-4" />

            <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
              <div className="metric-card">
                <p className="text-sm text-[var(--text-muted)] mb-1">目前市值</p>
                {position.currentValueHome != null ? (
                  <>
                    <p className="text-xl font-bold text-[var(--text-primary)] number-display">
                      {formatTWD(position.currentValueHome)}
                    </p>
                    <p className="text-sm text-[var(--text-muted)]">{homeCurrency}</p>
                  </>
                ) : (
                  <>
                    <Skeleton width="w-24" height="h-7" />
                    <Skeleton width="w-12" height="h-5" />
                  </>
                )}
              </div>

              <div className="metric-card">
                <p className="text-sm text-[var(--text-muted)] mb-1">未實現損益</p>
                {position.unrealizedPnlHome != null ? (
                  <>
                    <p className={`text-xl font-bold number-display ${pnlColor}`}>
                      {formatTWD(position.unrealizedPnlHome)}
                    </p>
                    <p className={`text-sm ${pnlColor}`}>
                      {formatPercent(position.unrealizedPnlPercentage)}
                    </p>
                  </>
                ) : (
                  <>
                    <Skeleton width="w-24" height="h-7" />
                    <Skeleton width="w-16" height="h-5" />
                  </>
                )}
              </div>

              {position.unrealizedPnlSource != null ? (
                <div className="metric-card">
                  <p className="text-sm text-[var(--text-muted)] mb-1">原幣未實現損益</p>
                  <p className={`text-xl font-bold number-display ${position.unrealizedPnlSource >= 0 ? 'number-positive' : 'number-negative'}`}>
                    {formatSignedNumber(position.unrealizedPnlSource, 2)}
                  </p>
                  <p className={`text-sm ${position.unrealizedPnlSource >= 0 ? 'number-positive' : 'number-negative'}`}>
                    {formatPercent(position.unrealizedPnlSourcePercentage)}
                  </p>
                </div>
              ) : (
                <div className="metric-card">
                  <p className="text-sm text-[var(--text-muted)] mb-1">原幣未實現損益</p>
                  {position.unrealizedPnlSourcePercentage != null ? (
                     // Should not happen if source null, but for completeness
                     null
                  ) : (
                    <>
                      <Skeleton width="w-24" height="h-7" />
                      <Skeleton width="w-16" height="h-5" />
                    </>
                  )}
                </div>
              )}

              {/* Only show this placeholder if we expect source PnL but don't have it yet?
                  Actually, logic above is: if unPnlSource != null show card.
                  If it is null, we might be loading OR it might be local stock.
                  We need to know if we SHOULD show it.
                  If local stock, unPnlSource might be null/undefined forever.

                  Let's check the logic:
                  If isLoading, we show Skeletons.
                  If loaded, we conditionally render.

                  Wait, if I change the grid to cols-4, and I have 3 items (local stock), I get 1 empty slot.
                  That is fine.

                  The issue is "XIRR on its own row".
                  If I have 4 items, cols-3 puts XIRR on row 2.
                  So cols-4 fixes that.
              */}

              <div className="metric-card">
                <p className="text-sm text-[var(--text-muted)] mb-1">年化報酬率 (XIRR)</p>
                {positionXirr?.xirrPercentage != null ? (
                  <p className={`text-xl font-bold number-display ${positionXirr.xirrPercentage >= 0 ? 'number-positive' : 'number-negative'}`}>
                    {formatPercent(positionXirr.xirrPercentage)}
                  </p>
                ) : (
                  <Skeleton width="w-24" height="h-7" />
                )}
                <p className="text-sm text-[var(--text-muted)]">
                  {positionXirr?.cashFlowCount ? `含 ${positionXirr.cashFlowCount} 筆現金流` : ''}
                </p>
              </div>
            </div>
          </div>
        ) : (
          <div className="card-dark p-8 text-center mb-6">
            <p className="text-[var(--text-muted)]">目前無持倉（可能已全數賣出）</p>
          </div>
        )}

        {/* Transaction List */}
        <div className="card-dark overflow-hidden">
          <div className="px-5 py-4 border-b border-[var(--border-color)] flex justify-between items-center">
            <h2 className="text-lg font-bold text-[var(--text-primary)]">
              {ticker} 交易紀錄
            </h2>
            <FileDropdown
              onExport={handleExportTransactions}
              exportDisabled={transactions.length === 0}
            />
          </div>
          {transactions.length > 0 ? (
            <TransactionList
              transactions={transactions}
              onDelete={handleDeleteClick}
            />
          ) : (
            <div className="p-8 text-center text-[var(--text-muted)]">
              尚無交易紀錄
            </div>
          )}
        </div>
      </div>

      {/* Delete Confirmation Modal */}
      <ConfirmationModal
        isOpen={showDeleteModal}
        onClose={() => setShowDeleteModal(false)}
        onConfirm={handleConfirmDelete}
        title="刪除交易"
        message="確定要刪除此交易紀錄嗎？此動作無法復原。"
        confirmText="刪除"
        isDestructive={true}
      />
    </div>
  );
}
