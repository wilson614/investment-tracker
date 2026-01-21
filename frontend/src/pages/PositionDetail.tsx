/**
 * Position Detail Page
 *
 * 單一持倉詳情頁：顯示該 ticker 的持倉資訊、交易明細、即時報價與單一持倉 XIRR。
 *
 * 特色：
 * - 進頁面先套用 localStorage 快取 quote / XIRR 讓數字立即可見，再自動抓最新報價。
 * - 報價支援 US 失敗時的 UK fallback。
 */
import { useState, useEffect, useCallback, useRef } from 'react';
import { useParams, Link } from 'react-router-dom';
import { ArrowLeft, RefreshCw, Loader2 } from 'lucide-react';
import { portfolioApi, transactionApi, stockPriceApi } from '../services/api';
import { exportTransactionsToCsv } from '../services/csvExport';
import { TransactionList } from '../components/transactions/TransactionList';
import { FileDropdown } from '../components/common';
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
const getQuoteCacheKey = (ticker: string) => `quote_cache_${ticker}`;
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
const loadCachedQuote = (ticker: string): { quote: StockQuoteResponse | null; updatedAt: Date | null; market: StockMarketType } => {
  try {
    const cached = localStorage.getItem(getQuoteCacheKey(ticker));
    if (cached) {
      const data: CachedQuote = JSON.parse(cached);
      return {
        quote: data.quote,
        updatedAt: new Date(data.updatedAt),
        market: data.market,
      };
    }
  } catch {
    // Ignore cache errors
  }
  return { quote: null, updatedAt: null, market: guessMarket(ticker) };
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
  const { ticker } = useParams<{ ticker: string }>();
  const [portfolio, setPortfolio] = useState<Portfolio | null>(null);
  const [portfolioId, setPortfolioId] = useState<string | null>(null);
  const [position, setPosition] = useState<StockPosition | null>(null);
  const [transactions, setTransactions] = useState<StockTransaction[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Load cached quote on init (use useRef to avoid re-creating on every render)
  const cachedDataRef = useRef(ticker ? loadCachedQuote(ticker) : { quote: null, updatedAt: null, market: StockMarket.US as StockMarketType });
  const cachedData = cachedDataRef.current;
  // XIRR cache will be loaded after we have portfolioId
  const cachedXirrRef = useRef<XirrResult | null>(null);

  // Quote state - initialize from cache
  const [selectedMarket, setSelectedMarket] = useState<StockMarketType>(cachedData.market);
  const [fetchStatus, setFetchStatus] = useState<'idle' | 'loading' | 'success' | 'error'>(
    cachedData.quote ? 'success' : 'idle'
  );
  const [lastQuote, setLastQuote] = useState<StockQuoteResponse | null>(cachedData.quote);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(cachedData.updatedAt);
  const [positionXirr, setPositionXirr] = useState<XirrResult | null>(cachedXirrRef.current);

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
      setIsLoading(true);
      setError(null);

      // Get user's portfolio
      const portfolios = await portfolioApi.getAll();
      if (portfolios.length === 0) {
        setError('找不到投資組合');
        return;
      }
      const currentPortfolioId = portfolios[0].id;
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

      // Find position for this ticker
      const pos = summaryData.positions.find(
        (p) => p.ticker.toUpperCase() === ticker.toUpperCase()
      );

      // If we have cached quote, apply it immediately to position
      if (pos && cachedData.quote && cachedData.quote.exchangeRate) {
        const currentValue = pos.totalShares * cachedData.quote.price * cachedData.quote.exchangeRate;
        const totalCostHome = pos.totalCostHome ?? 0;
        const pnl = currentValue - totalCostHome;
        const pnlPct = totalCostHome > 0 ? (pnl / totalCostHome) * 100 : 0;
        setPosition({
          ...pos,
          currentPrice: cachedData.quote.price,
          currentExchangeRate: cachedData.quote.exchangeRate,
          currentValueHome: currentValue,
          unrealizedPnlHome: pnl,
          unrealizedPnlPercentage: pnlPct,
        });
        hasAppliedCache.current = true;
      } else {
        setPosition(pos || null);
      }

      // Filter transactions for this ticker
      const tickerTx = txData.filter(
        (t) => t.ticker.toUpperCase() === ticker.toUpperCase()
      );
      setTransactions(tickerTx);
    } catch (err) {
      setError(err instanceof Error ? err.message : '載入失敗');
    } finally {
      setIsLoading(false);
    }
  }, [ticker]);

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
        }).catch(() => setPositionXirr(null));
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
   * 若選擇 US 且失敗，會自動 fallback 至 UK。
   */
  const handleFetchQuote = async () => {
    if (!portfolio || !portfolioId) return;

    setFetchStatus('loading');

    try {
      let quote = await stockPriceApi.getQuoteWithRate(
        selectedMarket,
        ticker!,
        portfolio.homeCurrency
      );

      // UK fallback for US market
      if (!quote && selectedMarket === StockMarket.US) {
        quote = await stockPriceApi.getQuoteWithRate(
          StockMarket.UK,
          ticker!,
          portfolio.homeCurrency
        );
        if (quote) {
          setSelectedMarket(StockMarket.UK);
        }
      }

      if (quote) {
        const now = new Date();
        setLastQuote(quote);
        setLastUpdated(now);
        setFetchStatus('success');

        // Save to localStorage cache (same format as PositionCard)
        try {
          localStorage.setItem(getQuoteCacheKey(ticker!), JSON.stringify({
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

          setPosition({
            ...position,
            currentPrice: quote.price,
            currentExchangeRate: quote.exchangeRate,
            currentValueHome: currentValue,
            unrealizedPnlHome: pnl,
            unrealizedPnlPercentage: pnlPct,
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
            // XIRR calculation failed, ignore
            setPositionXirr(null);
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
   * 刪除單筆交易後重新載入頁面資料。
   * @param transactionId 交易 ID
   */
  const handleDeleteTransaction = async (transactionId: string) => {
    if (!window.confirm('確定要刪除此交易紀錄嗎？')) return;
    await transactionApi.delete(transactionId);
    await loadData();
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

  if (isLoading) {
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
                <p className="text-xl font-bold text-[var(--text-primary)] number-display">
                  {lastQuote ? formatNumber(lastQuote.price) : '-'}
                </p>
                <p className="text-sm text-[var(--text-muted)]">
                  {lastQuote ? `${baseCurrency} · 匯率 ${formatNumber(lastQuote.exchangeRate ?? 0, 4)}` : baseCurrency}
                </p>
              </div>
            </div>

            {/* Show computed values section when we have current value (from cache or fresh quote) */}
            {position.currentValueHome != null && (
              <>
                <hr className="border-[var(--border-color)] my-4" />
                <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
                  <div className="metric-card">
                    <p className="text-sm text-[var(--text-muted)] mb-1">目前市值</p>
                    <p className="text-xl font-bold text-[var(--text-primary)] number-display">
                      {formatTWD(position.currentValueHome)}
                    </p>
                    <p className="text-sm text-[var(--text-muted)]">{homeCurrency}</p>
                  </div>

                  <div className="metric-card">
                    <p className="text-sm text-[var(--text-muted)] mb-1">未實現損益</p>
                    <p className={`text-xl font-bold number-display ${pnlColor}`}>
                      {formatTWD(position.unrealizedPnlHome)}
                    </p>
                    <p className={`text-sm ${pnlColor}`}>
                      {formatPercent(position.unrealizedPnlPercentage)}
                    </p>
                  </div>

                  <div className="metric-card">
                    <p className="text-sm text-[var(--text-muted)] mb-1">年化報酬率 (XIRR)</p>
                    {positionXirr?.xirrPercentage != null ? (
                      <p className={`text-xl font-bold number-display ${positionXirr.xirrPercentage >= 0 ? 'number-positive' : 'number-negative'}`}>
                        {formatPercent(positionXirr.xirrPercentage)}
                      </p>
                    ) : (
                      <p className="text-xl font-bold text-[var(--text-primary)] number-display">-</p>
                    )}
                    <p className="text-sm text-[var(--text-muted)]">
                      {positionXirr?.cashFlowCount ? `含 ${positionXirr.cashFlowCount} 筆現金流` : ''}
                    </p>
                  </div>
                </div>
              </>
            )}
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
              onDelete={handleDeleteTransaction}
            />
          ) : (
            <div className="p-8 text-center text-[var(--text-muted)]">
              尚無交易紀錄
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
