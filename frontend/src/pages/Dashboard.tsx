import { useState, useEffect, useRef } from 'react';
import { Link } from 'react-router-dom';
import { ChevronDown, RefreshCw, Loader2 } from 'lucide-react';
import { portfolioApi, stockPriceApi } from '../services/api';
import { PerformanceMetrics } from '../components/portfolio/PerformanceMetrics';
import { PositionCard } from '../components/portfolio/PositionCard';
import { StockMarket } from '../types';
import type { Portfolio, PortfolioSummary, XirrResult, CurrentPriceInfo, StockMarket as StockMarketType, StockQuoteResponse } from '../types';

interface PortfolioWithMetrics {
  portfolio: Portfolio;
  summary: PortfolioSummary | null;
  xirrResult: XirrResult | null;
  isLoading: boolean;
  error: string | null;
}

interface CachedQuote {
  quote: StockQuoteResponse;
  updatedAt: string;
  market: StockMarketType;
}

const getQuoteCacheKey = (ticker: string) => `quote_cache_${ticker}`;

// Load cached quotes for a list of tickers
const loadCachedPrices = (tickers: string[]): Record<string, CurrentPriceInfo> => {
  const prices: Record<string, CurrentPriceInfo> = {};
  const maxAge = 60 * 60 * 1000; // 1 hour max cache age for dashboard

  for (const ticker of tickers) {
    try {
      const cached = localStorage.getItem(getQuoteCacheKey(ticker));
      if (cached) {
        const data: CachedQuote = JSON.parse(cached);
        const cacheAge = Date.now() - new Date(data.updatedAt).getTime();
        if (cacheAge <= maxAge && data.quote.exchangeRate) {
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

export function DashboardPage() {
  const [portfolios, setPortfolios] = useState<Portfolio[]>([]);
  const [portfolioMetrics, setPortfolioMetrics] = useState<Map<string, PortfolioWithMetrics>>(new Map());
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedPortfolioId, setSelectedPortfolioId] = useState<string | null>(null);
  const [fetchingAllPortfolioId, setFetchingAllPortfolioId] = useState<string | null>(null);

  // Track current prices per portfolio
  const currentPricesRef = useRef<Map<string, Record<string, CurrentPriceInfo>>>(new Map());

  useEffect(() => {
    loadPortfolios();
  }, []);

  const loadPortfolios = async () => {
    try {
      setIsLoading(true);
      setError(null);
      const data = await portfolioApi.getAll();
      setPortfolios(data);

      const metricsMap = new Map<string, PortfolioWithMetrics>();
      await Promise.all(
        data.map(async (portfolio) => {
          try {
            // First get basic summary to know all tickers
            const basicSummary = await portfolioApi.getSummary(portfolio.id);
            const tickers = basicSummary.positions.map(p => p.ticker);

            // Load cached prices for all positions
            const cachedPrices = loadCachedPrices(tickers);
            currentPricesRef.current.set(portfolio.id, cachedPrices);

            // If we have cached prices, recalculate with them
            if (Object.keys(cachedPrices).length > 0) {
              const [summary, xirrResult] = await Promise.all([
                portfolioApi.getSummary(portfolio.id, cachedPrices),
                portfolioApi.calculateXirr(portfolio.id, { currentPrices: cachedPrices }),
              ]);
              metricsMap.set(portfolio.id, {
                portfolio,
                summary,
                xirrResult,
                isLoading: false,
                error: null,
              });
            } else {
              metricsMap.set(portfolio.id, {
                portfolio,
                summary: basicSummary,
                xirrResult: null,
                isLoading: false,
                error: null,
              });
            }
          } catch (err) {
            metricsMap.set(portfolio.id, {
              portfolio,
              summary: null,
              xirrResult: null,
              isLoading: false,
              error: err instanceof Error ? err.message : 'Failed to load',
            });
          }
        })
      );
      setPortfolioMetrics(metricsMap);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load portfolios');
    } finally {
      setIsLoading(false);
    }
  };

  const updateSummaryWithPrices = async (portfolioId: string, prices: Record<string, CurrentPriceInfo>) => {
    const metrics = portfolioMetrics.get(portfolioId);
    if (!metrics || Object.keys(prices).length === 0) return;

    setPortfolioMetrics((prev) => {
      const newMap = new Map(prev);
      newMap.set(portfolioId, { ...metrics, isLoading: true });
      return newMap;
    });

    try {
      const [summary, xirrResult] = await Promise.all([
        portfolioApi.getSummary(portfolioId, prices),
        portfolioApi.calculateXirr(portfolioId, { currentPrices: prices }),
      ]);

      setPortfolioMetrics((prev) => {
        const newMap = new Map(prev);
        newMap.set(portfolioId, {
          ...metrics,
          summary,
          xirrResult,
          isLoading: false,
          error: null,
        });
        return newMap;
      });
    } catch (err) {
      setPortfolioMetrics((prev) => {
        const newMap = new Map(prev);
        newMap.set(portfolioId, {
          ...metrics,
          isLoading: false,
          error: err instanceof Error ? err.message : 'Failed to calculate',
        });
        return newMap;
      });
    }
  };

  const handlePositionPriceUpdate = (portfolioId: string, ticker: string, price: number, exchangeRate: number) => {
    const portfolioPrices = currentPricesRef.current.get(portfolioId) || {};
    portfolioPrices[ticker] = { price, exchangeRate };
    currentPricesRef.current.set(portfolioId, portfolioPrices);
    updateSummaryWithPrices(portfolioId, { ...portfolioPrices });
  };

  const handleFetchAllPrices = async (portfolioId: string) => {
    const metrics = portfolioMetrics.get(portfolioId);
    if (!metrics?.summary) return;

    setFetchingAllPortfolioId(portfolioId);

    try {
      const homeCurrency = metrics.portfolio.homeCurrency;
      const fetchPromises = metrics.summary.positions.map(async (position) => {
        try {
          const market = guessMarket(position.ticker);
          let quote = await stockPriceApi.getQuoteWithRate(market, position.ticker, homeCurrency);

          // If US market fails, try UK as fallback (for ETFs like VWRA)
          if (!quote && market === StockMarket.US) {
            quote = await stockPriceApi.getQuoteWithRate(StockMarket.UK, position.ticker, homeCurrency);
          }

          if (quote?.exchangeRate) {
            return { ticker: position.ticker, price: quote.price, exchangeRate: quote.exchangeRate };
          }
          return null;
        } catch {
          // If US fails, try UK as fallback
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

      const existingPrices = currentPricesRef.current.get(portfolioId) || {};
      const allPrices = { ...existingPrices, ...newPrices };
      currentPricesRef.current.set(portfolioId, allPrices);

      if (Object.keys(newPrices).length > 0) {
        await updateSummaryWithPrices(portfolioId, allPrices);
      }
    } finally {
      setFetchingAllPortfolioId(null);
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

  const aggregateTotals = Array.from(portfolioMetrics.values()).reduce(
    (acc, metrics) => {
      if (metrics.summary) {
        acc.totalCost += metrics.summary.totalCostHome;
        if (metrics.summary.totalValueHome != null) {
          acc.totalValue += metrics.summary.totalValueHome;
          acc.hasValue = true;
        }
        if (metrics.summary.totalUnrealizedPnlHome != null) {
          acc.totalPnl += metrics.summary.totalUnrealizedPnlHome;
        }
        acc.positionCount += metrics.summary.positions.length;
      }
      if (metrics.xirrResult?.xirr != null) {
        acc.xirrSum += metrics.xirrResult.xirr;
        acc.xirrCount += 1;
      }
      return acc;
    },
    { totalCost: 0, totalValue: 0, totalPnl: 0, positionCount: 0, hasValue: false, xirrSum: 0, xirrCount: 0 }
  );

  // Calculate return percentage
  const returnPercentage = aggregateTotals.hasValue && aggregateTotals.totalCost > 0
    ? (aggregateTotals.totalPnl / aggregateTotals.totalCost) * 100
    : null;

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

  return (
    <div className="min-h-screen py-8">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <h1 className="text-2xl font-bold text-[var(--text-primary)] mb-8">儀表板</h1>

        {/* Aggregate Summary */}
        <div className="card-dark p-6 mb-8">
          <h2 className="text-xl font-bold text-[var(--text-primary)] mb-6">整體投資組合摘要</h2>
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
            <div className="metric-card metric-card-cream">
              <p className="text-[var(--text-muted)] text-sm mb-1">總成本</p>
              <p className="text-2xl font-bold text-[var(--accent-cream)] number-display">
                {formatTWD(aggregateTotals.totalCost)}
              </p>
              <p className="text-[var(--text-muted)] text-sm">TWD</p>
            </div>
            <div className="metric-card metric-card-sand">
              <p className="text-[var(--text-muted)] text-sm mb-1">目前市值</p>
              <p className="text-2xl font-bold text-[var(--accent-sand)] number-display">
                {aggregateTotals.hasValue ? formatTWD(aggregateTotals.totalValue) : '-'}
              </p>
              <p className="text-[var(--text-muted)] text-sm">TWD</p>
            </div>
            <div className="metric-card metric-card-peach">
              <p className="text-[var(--text-muted)] text-sm mb-1">未實現損益</p>
              <p className={`text-2xl font-bold number-display ${aggregateTotals.totalPnl >= 0 ? 'number-positive' : 'number-negative'}`}>
                {aggregateTotals.hasValue ? formatTWD(aggregateTotals.totalPnl) : '-'}
              </p>
              <p className={`text-sm ${returnPercentage != null && returnPercentage >= 0 ? 'number-positive' : returnPercentage != null ? 'number-negative' : 'text-[var(--text-muted)]'}`}>
                {formatPercent(returnPercentage)}
              </p>
            </div>
            <div className="metric-card metric-card-blush">
              <p className="text-[var(--text-muted)] text-sm mb-1">持倉數量</p>
              <p className="text-2xl font-bold text-[var(--accent-blush)] number-display">
                {aggregateTotals.positionCount}
              </p>
            </div>
          </div>
        </div>

        {/* Portfolio List */}
        {portfolios.length === 0 ? (
          <div className="card-dark p-8 text-center">
            <p className="text-[var(--text-muted)] text-lg mb-4">尚無投資組合</p>
            <Link
              to="/"
              className="btn-accent inline-block"
            >
              建立投資組合
            </Link>
          </div>
        ) : (
          <div className="space-y-4">
            {portfolios.map((portfolio) => {
              const metrics = portfolioMetrics.get(portfolio.id);
              const isSelected = selectedPortfolioId === portfolio.id;
              const isFetchingAll = fetchingAllPortfolioId === portfolio.id;

              return (
                <div key={portfolio.id} className="card-dark overflow-hidden">
                  <div
                    className="px-6 py-5 flex justify-between items-center cursor-pointer hover:bg-[var(--bg-hover)] transition-colors"
                    onClick={() => setSelectedPortfolioId(isSelected ? null : portfolio.id)}
                  >
                    <div>
                      <h3 className="text-lg font-semibold text-[var(--text-primary)]">投資組合</h3>
                    </div>
                    <div className="flex items-center gap-4">
                      <Link
                        to={`/portfolio/${portfolio.id}`}
                        className="text-[var(--accent-peach)] hover:underline text-base"
                        onClick={(e) => e.stopPropagation()}
                      >
                        查看詳情 →
                      </Link>
                      <ChevronDown
                        className={`w-6 h-6 text-[var(--text-muted)] transform transition-transform ${isSelected ? 'rotate-180' : ''}`}
                      />
                    </div>
                  </div>

                  {isSelected && metrics?.summary && (
                    <div className="p-6 border-t border-[var(--border-color)] space-y-6">
                      {/* Fetch All Button and Positions */}
                      {metrics.summary.positions.length > 0 && (
                        <div>
                          <div className="flex items-center justify-between mb-4">
                            <h4 className="text-lg font-semibold text-[var(--text-primary)]">持倉</h4>
                            <button
                              type="button"
                              onClick={(e) => {
                                e.stopPropagation();
                                handleFetchAllPrices(portfolio.id);
                              }}
                              disabled={isFetchingAll || metrics.isLoading}
                              className="btn-dark flex items-center gap-2 px-4 py-2 text-sm disabled:opacity-50"
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
                            {metrics.summary.positions.map((position) => (
                              <PositionCard
                                key={position.ticker}
                                position={position}
                                baseCurrency={portfolio.baseCurrency}
                                homeCurrency={portfolio.homeCurrency}
                                onPriceUpdate={(ticker, price, exchangeRate) =>
                                  handlePositionPriceUpdate(portfolio.id, ticker, price, exchangeRate)
                                }
                              />
                            ))}
                          </div>
                        </div>
                      )}

                      <PerformanceMetrics
                        summary={metrics.summary}
                        xirrResult={metrics.xirrResult}
                        homeCurrency={portfolio.homeCurrency}
                        isLoading={metrics.isLoading}
                      />
                    </div>
                  )}

                  {isSelected && metrics?.error && (
                    <div className="p-6 border-t border-[var(--border-color)]">
                      <p className="text-[var(--color-danger)] text-base">{metrics.error}</p>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
