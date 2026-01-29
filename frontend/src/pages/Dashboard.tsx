/**
 * Dashboard Page
 *
 * 顯示投資組合的整體概況（總成本/市值/未實現損益/XIRR）、資產配置圖、持倉績效與近期交易。
 *
 * 特色：
 * - 先顯示 localStorage 的快取報價，再背景更新最新報價，減少使用者等待時間。
 * - 針對部分 ETF（例如 VWRA）在 US 報價失敗時，會改用 UK 市場作為 fallback。
 */
import { useState, useEffect, useRef } from 'react';
import { RefreshCw, Loader2, TrendingUp, TrendingDown } from 'lucide-react';
import { portfolioApi, stockPriceApi, transactionApi, marketDataApi } from '../services/api';
import { MarketContext, MarketYtdSection, HistoricalValueChart } from '../components/dashboard';
import { AssetAllocationPieChart } from '../components/charts';
import { XirrWarningBadge } from '../components/common/XirrWarningBadge';
import { Skeleton } from '../components/common/SkeletonLoader';
import { usePortfolio } from '../contexts/PortfolioContext';
import { StockMarket, TransactionType } from '../types';
import type { Portfolio, PortfolioSummary, XirrResult, CurrentPriceInfo, StockMarket as StockMarketType, StockQuoteResponse, StockTransaction } from '../types';
import { refreshCapeData } from '../services/capeApi';
import { refreshYtdData } from '../services/ytdApi';
import { formatShortDate } from '../utils/dateUtils';

interface CachedQuote {
  quote: StockQuoteResponse;
  updatedAt: string;
  market: StockMarketType;
}

const getQuoteCacheKey = (ticker: string, market?: StockMarketType) =>
  `quote_cache_${ticker}_${market ?? 'default'}`;

/**
 * 從 localStorage 載入指定 ticker 的快取報價。
 *
 * 設計重點：
 * - 不設定時效限制：先用快取讓 UI 立即有數字，再由使用者/自動更新取得最新報價。
 * - 只有在快取資料包含 exchangeRate 時，才會回填到 currentPrices。
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

/**
 * 依 ticker 格式推測市場別。
 *
 * 規則：
 * - TW：純數字或數字+英文字尾（例如 `2330`、`00878`、`6547M`）
 * - UK：以 `.L` 結尾（London Stock Exchange）
 * - 其他：預設 US
 */
const guessMarket = (ticker: string): StockMarketType => {
  // Taiwan: pure digits, or digits ending with letters (e.g., 2330, 00878, 6547M)
  if (/^\d+[A-Za-z]*$/.test(ticker)) {
    return StockMarket.TW;
  }
  // UK: ends with .L (London Stock Exchange)
  if (ticker.endsWith('.L')) {
    return StockMarket.UK;
  }
  // Default to US
  return StockMarket.US;
};

interface PositionWithPnl {
  ticker: string;
  totalShares: number;
  avgCostPerShareHome?: number;
  currentPrice?: number;
  pnlPercentage?: number;
  valueHome?: number;
  unrealizedPnlHome?: number;
  weight?: number; // FR-131: position weight as % of total portfolio
}

interface HistoricalMonthValue {
  month: string;
  value: number | null;
  contributions: number | null;
}

export function DashboardPage() {
  const { currentPortfolioId } = usePortfolio();

  const [portfolio, setPortfolio] = useState<Portfolio | null>(null);
  const [summary, setSummary] = useState<PortfolioSummary | null>(null);
  const [xirrResult, setXirrResult] = useState<XirrResult | null>(null);
  const [recentTransactions, setRecentTransactions] = useState<StockTransaction[]>([]);
  const [historicalData, setHistoricalData] = useState<HistoricalMonthValue[]>([]);
  const [isLoadingHistorical, setIsLoadingHistorical] = useState(true);
  const [isLoading, setIsLoading] = useState(true);
  const [isFetchingPrices, setIsFetchingPrices] = useState(false);
  const [isPriceDataPending, setIsPriceDataPending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const currentPricesRef = useRef<Record<string, CurrentPriceInfo>>({});

  // Track if we need to auto-fetch prices after initial load
  const shouldAutoFetch = useRef(true);

  // When switching portfolio, allow auto-fetch again
  useEffect(() => {
    shouldAutoFetch.current = true;
    currentPricesRef.current = {};
    setIsPriceDataPending(false);
    setXirrResult(null);
  }, [currentPortfolioId]);

  useEffect(() => {
    loadDashboardData();
  }, [currentPortfolioId]);

  // Auto-fetch prices after initial data load
  useEffect(() => {
    if (!isLoading && portfolio && summary && shouldAutoFetch.current) {
      shouldAutoFetch.current = false;

      // 若沒有任何報價快取（例如登出已清除），這段期間不顯示 0 / '-' 的估值結果
      // 直到抓到即時報價並重新計算 summary/XIRR。
      const hasAnyCachedPrices = Object.keys(currentPricesRef.current).length > 0;
      if (summary.positions.length > 0 && !hasAnyCachedPrices) {
        setIsPriceDataPending(true);
      }

      handleFetchAllPrices();
    }
  }, [isLoading, portfolio, summary]);

  // Load historical performance data for the chart
  useEffect(() => {
    if (!portfolio) return;

    const loadHistoricalData = async () => {
      setIsLoadingHistorical(true);
      try {
        const result = await portfolioApi.getMonthlyNetWorth(portfolio.id);
        setHistoricalData(
          result.data.map((d) => ({
            month: d.month,
            value: d.value,
            contributions: d.contributions,
          }))
        );
      } catch {
        // Silently fail - chart will show "no data" message
        setHistoricalData([]);
      } finally {
        setIsLoadingHistorical(false);
      }
    };

    loadHistoricalData();
  }, [portfolio?.id]);

  /**
   * 取得 Dashboard 需要的初始資料。
   *
   * 流程：
   * 1) 取得目前選定的投資組合（由 PortfolioContext 提供 currentPortfolioId）
   * 2) 並行載入 summary 與交易清單
   * 3) 讀取快取報價並先用快取重新計算 summary / XIRR（若快取存在）
   */
  const loadDashboardData = async () => {
    try {
      setIsLoading(true);
      setError(null);

      if (!currentPortfolioId) {
        setPortfolio(null);
        setSummary(null);
        setXirrResult(null);
        setRecentTransactions([]);
        setHistoricalData([]);
        setIsLoadingHistorical(false);
        setIsLoading(false);
        return;
      }

      const p = await portfolioApi.getById(currentPortfolioId);
      setPortfolio(p);

      // Load summary and transactions in parallel
      const [basicSummary, txData] = await Promise.all([
        portfolioApi.getSummary(p.id),
        transactionApi.getByPortfolio(p.id),
      ]);

      const cachedPrices = loadCachedPrices(basicSummary.positions);
      currentPricesRef.current = cachedPrices;

      // Get most recent 5 transactions
      const sortedTx = [...txData].sort((a, b) =>
        new Date(b.transactionDate).getTime() - new Date(a.transactionDate).getTime()
      );
      setRecentTransactions(sortedTx.slice(0, 5));

      // If we have cached prices, recalculate with them
      if (Object.keys(cachedPrices).length > 0) {
        const [summaryWithPrices, xirr] = await Promise.all([
          portfolioApi.getSummary(p.id, cachedPrices),
          portfolioApi.calculateXirr(p.id, { currentPrices: cachedPrices }),
        ]);
        setSummary(summaryWithPrices);
        setXirrResult(xirr);
      } else {
        setSummary(basicSummary);
        setXirrResult(null);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load dashboard');
    } finally {
      setIsLoading(false);
    }
  };

  /**
   * 取得所有持倉的最新報價（含匯率），並更新 summary / XIRR。
   *
   * 同時會刷新市場資料（CAPE、YTD），但即使報價失敗也會盡量保留快取結果。
   */
  const handleFetchAllPrices = async () => {
    if (!portfolio || !summary) return;

    setIsFetchingPrices(true);

    try {
      // Refresh market data (CAPE, YTD) in parallel with stock prices
      const marketDataPromise = Promise.allSettled([
        refreshCapeData(),
        refreshYtdData(),
      ]);

      // If there are no positions, still refresh market data and exit
      if (summary.positions.length === 0) {
        await marketDataPromise;
        return;
      }

      const homeCurrency = portfolio.homeCurrency;
      const fetchPromises = summary.positions.map(async (position) => {
        try {
          const market = position.market ?? guessMarket(position.ticker);

          // EU 市場使用 Euronext API
          if (market === StockMarket.EU) {
            const euronextQuote = await marketDataApi.getEuronextQuoteByTicker(
              position.ticker,
              homeCurrency
            );
            if (euronextQuote?.exchangeRate) {
              const syntheticQuote: StockQuoteResponse = {
                symbol: position.ticker,
                name: euronextQuote.name || position.ticker,
                price: euronextQuote.price,
                market: StockMarket.EU as StockMarketType,
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
                market: StockMarket.EU as StockMarketType,
              };
              localStorage.setItem(getQuoteCacheKey(position.ticker, StockMarket.EU as StockMarketType), JSON.stringify(cacheData));
              return { ticker: position.ticker, price: euronextQuote.price, exchangeRate: euronextQuote.exchangeRate };
            }
            return null;
          }

          let quote = await stockPriceApi.getQuoteWithRate(market, position.ticker, homeCurrency);
          let finalMarket = market;

          // 若預設市場是 US，但報價失敗，改用 UK 作為備援（例如 VWRA 等在 LSE 掛牌的 ETF）。
          if (!quote && market === StockMarket.US) {
            quote = await stockPriceApi.getQuoteWithRate(StockMarket.UK, position.ticker, homeCurrency);
            if (quote) finalMarket = StockMarket.UK;
          }

          if (quote?.exchangeRate) {
            // 快取完整 quote，讓下次進入 Dashboard 能先顯示上次結果。
            const cacheData: CachedQuote = {
              quote,
              updatedAt: new Date().toISOString(),
              market: finalMarket,
            };
            localStorage.setItem(getQuoteCacheKey(position.ticker, finalMarket), JSON.stringify(cacheData));
            return { ticker: position.ticker, price: quote.price, exchangeRate: quote.exchangeRate };
          }
          return null;
        } catch {
          // 如果一開始推測為 US，失敗後再嘗試 UK（與上方 !quote 分支互補）。
          const market = position.market ?? guessMarket(position.ticker);
          if (market === StockMarket.US) {
            try {
              const ukQuote = await stockPriceApi.getQuoteWithRate(StockMarket.UK, position.ticker, homeCurrency);
              if (ukQuote?.exchangeRate) {
                const cacheData: CachedQuote = {
                  quote: ukQuote,
                  updatedAt: new Date().toISOString(),
                  market: StockMarket.UK,
                };
                localStorage.setItem(getQuoteCacheKey(position.ticker, StockMarket.UK), JSON.stringify(cacheData));
                return { ticker: position.ticker, price: ukQuote.price, exchangeRate: ukQuote.exchangeRate };
              }
            } catch {
              // UK 也失敗時就略過
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

      const allPrices = { ...currentPricesRef.current, ...newPrices };
      currentPricesRef.current = allPrices;

      // Wait for market data refresh to complete (fire and forget, but wait)
      await marketDataPromise;

      if (Object.keys(newPrices).length > 0) {
        const [summaryWithPrices, xirr] = await Promise.all([
          portfolioApi.getSummary(portfolio.id, allPrices),
          portfolioApi.calculateXirr(portfolio.id, { currentPrices: allPrices }),
        ]);
        setSummary(summaryWithPrices);
        setXirrResult(xirr);
      }
    } finally {
      setIsPriceDataPending(false);
      setIsFetchingPrices(false);
    }
  };

  // Format TWD as integer
  const formatTWD = (value: number | null | undefined) => {
    if (value == null) return '-';
    return Math.round(value).toLocaleString('zh-TW');
  };

  // Format percentage
  const formatPercent = (value: number | null | undefined) => {
    if (value == null) return '-';
    const sign = value >= 0 ? '+' : '';
    return `${sign}${value.toFixed(2)}%`;
  };

  /**
   * 以 summary 為基礎，整理出 UI 需要的持倉績效欄位。
   *
   * 包含：
   * - pnlPercentage / unrealizedPnlHome：未實現損益（% 與本位幣金額）
   * - weight：FR-131，依持倉市值 / 總市值計算權重（%）
   */
  const getPositionsWithPnl = (): PositionWithPnl[] => {
    if (!summary) return [];

    const totalValue = summary.totalValueHome ?? 0;
    return summary.positions.map(pos => ({
      ticker: pos.ticker,
      totalShares: pos.totalShares,
      avgCostPerShareHome: pos.averageCostPerShareHome,
      currentPrice: pos.currentPrice,
      pnlPercentage: pos.unrealizedPnlPercentage,
      valueHome: pos.currentValueHome,
      unrealizedPnlHome: pos.unrealizedPnlHome,
      weight: totalValue > 0 && pos.currentValueHome != null 
        ? (pos.currentValueHome / totalValue) * 100 
        : undefined,
    }));
  };

  // 依未實現損益率排序，取表現最佳的持倉（給 UI 顯示 Top performers）。
  const getTopPerformers = () => {
    return getPositionsWithPnl()
      .filter(p => p.pnlPercentage != null)
      .sort((a, b) => (b.pnlPercentage ?? 0) - (a.pnlPercentage ?? 0));
  };

  /**
   * 計算資產配置（各持倉市值佔比）。
   *
   * 注意：僅納入有 `currentValueHome` 的持倉，並依百分比由大到小排序。
   */
  const getAssetAllocation = () => {
    if (!summary?.totalValueHome) return [];

    return summary.positions
      .filter(p => p.currentValueHome != null)
      .map(p => ({
        ticker: p.ticker,
        value: p.currentValueHome!,
        percentage: (p.currentValueHome! / summary.totalValueHome!) * 100,
      }))
      .sort((a, b) => b.percentage - a.percentage);
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

  if (!portfolio) {
    return (
      <div className="min-h-screen py-8">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <h1 className="text-2xl font-bold text-[var(--text-primary)] mb-8">儀表板</h1>

          {/* Market Context - CAPE & YTD (always show even without portfolio) */}
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mb-6">
            <MarketContext />
            <MarketYtdSection />
          </div>

          <div className="card-dark p-8 text-center">
            <p className="text-[var(--text-muted)] text-lg">尚無投資組合，請先建立一個投資組合。</p>
          </div>
        </div>
      </div>
    );
  }

  const hasValueData = summary?.totalValueHome != null;
  const isSummaryReady = hasValueData && !isPriceDataPending;
  const returnPercentage = isSummaryReady && summary?.totalCostHome
    ? ((summary.totalUnrealizedPnlHome ?? 0) / summary.totalCostHome) * 100
    : null;

  const topPerformers = getTopPerformers();
  const assetAllocation = getAssetAllocation();

  return (
    <div className="min-h-screen py-8">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="flex items-center justify-between mb-8">
          <h1 className="text-2xl font-bold text-[var(--text-primary)]">儀表板</h1>
          <button
            onClick={handleFetchAllPrices}
            disabled={isFetchingPrices || !summary}
            className="btn-dark flex items-center gap-2 px-4 py-2 text-sm disabled:opacity-50"
          >
            {isFetchingPrices ? (
              <Loader2 className="w-4 h-4 animate-spin" />
            ) : (
              <RefreshCw className="w-4 h-4" />
            )}
            更新全部
          </button>
        </div>

        {/* Market Context - CAPE & YTD */}
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mb-6">
          <MarketContext />
          <MarketYtdSection />
        </div>

        {/* Portfolio Summary */}
        <div className="card-dark p-6 mb-6">
          <div className="flex items-center gap-2 mb-4">
            <h2 className="text-lg font-bold text-[var(--text-primary)]">投資組合總覽</h2>
            {isPriceDataPending && (
              <div className="flex items-center gap-2 text-xs text-[var(--text-muted)]">
                <Loader2 className="w-3.5 h-3.5 animate-spin text-[var(--accent-peach)]" />
                <span>計算中...</span>
              </div>
            )}
          </div>
          <div className="grid grid-cols-2 lg:grid-cols-5 gap-4">
            <div className="metric-card">
              <p className="text-[var(--text-muted)] text-sm mb-1">總成本</p>
              <p className="text-xl font-bold text-[var(--text-primary)] number-display">
                {isPriceDataPending ? (
                  <Skeleton width="w-24" height="h-7" className="mt-1" />
                ) : (
                  formatTWD(summary?.totalCostHome)
                )}
              </p>
              <p className="text-[var(--text-muted)] text-sm">TWD</p>
            </div>
            <div className="metric-card">
              <p className="text-[var(--text-muted)] text-sm mb-1">目前市值</p>
              <p className="text-xl font-bold text-[var(--text-primary)] number-display">
                {isSummaryReady ? (
                  formatTWD(summary?.totalValueHome)
                ) : (
                  <Skeleton width="w-24" height="h-7" className="mt-1" />
                )}
              </p>
              <p className="text-[var(--text-muted)] text-sm">TWD</p>
            </div>
            <div className="metric-card">
              <p className="text-[var(--text-muted)] text-sm mb-1">未實現損益</p>
              <p className={`text-xl font-bold number-display ${isSummaryReady && (summary?.totalUnrealizedPnlHome ?? 0) >= 0 ? 'number-positive' : isSummaryReady ? 'number-negative' : ''}`}>
                {isSummaryReady ? (
                  formatTWD(summary?.totalUnrealizedPnlHome)
                ) : (
                  <Skeleton width="w-24" height="h-7" className="mt-1" />
                )}
              </p>
              <p className={`text-sm ${returnPercentage != null && returnPercentage >= 0 ? 'number-positive' : returnPercentage != null ? 'number-negative' : 'text-[var(--text-muted)]'}`}>
                {isSummaryReady && returnPercentage != null ? (
                  formatPercent(returnPercentage)
                ) : (
                  <Skeleton width="w-16" height="h-4" className="mt-1" />
                )}
              </p>
            </div>
            <div className="metric-card">
              <p className="text-[var(--text-muted)] text-sm mb-1">年化報酬 (XIRR)</p>
              {isSummaryReady && xirrResult?.xirrPercentage != null ? (
                <div className="flex items-center gap-1">
                  <p className={`text-xl font-bold number-display ${xirrResult.xirrPercentage >= 0 ? 'number-positive' : 'number-negative'}`}>
                    {formatPercent(xirrResult.xirrPercentage)}
                  </p>
                  <XirrWarningBadge
                    earliestTransactionDate={xirrResult.earliestTransactionDate}
                    asOfDate={xirrResult.asOfDate}
                  />
                </div>
              ) : (
                <Skeleton width="w-16" height="h-7" className="mt-1" />
              )}
              {isSummaryReady && xirrResult && xirrResult.cashFlowCount > 1 && (
                <p className="text-sm text-[var(--text-muted)]">
                  {xirrResult.cashFlowCount - 1} 筆交易
                </p>
              )}
            </div>
            <div className="metric-card">
              <p className="text-[var(--text-muted)] text-sm mb-1">持倉數量</p>
              <p className="text-xl font-bold text-[var(--text-primary)] number-display">
                {summary?.positions.length ?? 0}
              </p>
            </div>
          </div>
        </div>

        {/* Historical Portfolio Value Chart */}
        <div className="card-dark p-6 mb-6" style={{ minHeight: 356 }}>
          <h2 className="text-lg font-bold text-[var(--text-primary)] mb-4">歷史淨值變化</h2>
          {isLoadingHistorical ? (
            <Skeleton width="w-full" height="h-[280px]" />
          ) : historicalData.length > 0 ? (
            <HistoricalValueChart
              data={historicalData}
              currency={portfolio.homeCurrency}
              height={280}
            />
          ) : (
            <div className="flex items-center justify-center h-[280px] text-[var(--text-muted)]">
              無歷史資料
            </div>
          )}
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          {/* Asset Allocation */}
          <div className="card-dark p-6">
            <h2 className="text-lg font-bold text-[var(--text-primary)] mb-4">資產配置</h2>
            <AssetAllocationPieChart
              data={assetAllocation}
              homeCurrency={portfolio.homeCurrency}
            />
          </div>

          {/* Position Performance - FR-131: Show PnL (TWD) and PnL % */}
          <div className="card-dark p-6 flex flex-col">
            <h2 className="text-lg font-bold text-[var(--text-primary)] mb-4">持倉績效</h2>
            {topPerformers.length > 0 ? (
              <div className="space-y-3">
                {topPerformers.map((pos) => (
                  <div key={pos.ticker} className="flex items-center justify-between py-2 border-b border-[var(--border-color)] last:border-b-0">
                    <div className="flex items-center gap-3">
                      <span className="text-[var(--text-primary)] font-medium">{pos.ticker}</span>
                      <span className={`text-xs font-mono ${(pos.pnlPercentage ?? 0) >= 0 ? 'number-positive' : 'number-negative'}`}>
                        {pos.pnlPercentage != null ? `${pos.pnlPercentage >= 0 ? '+' : ''}${pos.pnlPercentage.toFixed(1)}%` : '-'}
                      </span>
                    </div>
                    <div className="flex items-center gap-2">
                      {(pos.unrealizedPnlHome ?? 0) >= 0 ? (
                        <TrendingUp className="w-4 h-4 text-green-500" />
                      ) : (
                        <TrendingDown className="w-4 h-4 text-red-500" />
                      )}
                      <span className={`font-mono font-medium ${(pos.unrealizedPnlHome ?? 0) >= 0 ? 'number-positive' : 'number-negative'}`}>
                        {pos.unrealizedPnlHome != null ? formatTWD(pos.unrealizedPnlHome) : '-'}
                      </span>
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <div className="flex-1 flex items-center justify-center">
                <p className="text-[var(--text-muted)] text-center">
                  獲取報價後顯示持倉績效
                </p>
              </div>
            )}
          </div>
        </div>

        {/* Recent Transactions */}
        {recentTransactions.length > 0 && (
          <div className="card-dark p-6 mt-6">
            <h2 className="text-lg font-bold text-[var(--text-primary)] mb-4">最近交易</h2>
            <div className="space-y-2">
              {recentTransactions.map((tx) => (
                <div key={tx.id} className="flex items-center justify-between py-2 border-b border-[var(--border-color)] last:border-b-0">
                  <div className="flex items-center gap-4">
                    <span className={`text-sm font-medium px-2 py-0.5 rounded ${tx.transactionType === TransactionType.Buy ? 'bg-green-500/20 text-green-400' : 'bg-red-500/20 text-red-400'}`}>
                      {tx.transactionType === TransactionType.Buy ? '買入' : '賣出'}
                    </span>
                    <span className="text-[var(--text-primary)] font-medium">{tx.ticker}</span>
                    <span className="text-sm text-[var(--text-muted)]">{tx.shares} 股</span>
                  </div>
                  <span className="text-sm text-[var(--text-muted)]">{formatShortDate(tx.transactionDate)}</span>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
