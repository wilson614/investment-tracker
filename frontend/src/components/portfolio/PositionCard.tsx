import { useState, useEffect, useRef, useCallback } from 'react';
import { stockPriceApi, marketDataApi, etfClassificationApi } from '../../services/api';
import type { StockPosition, StockMarket as StockMarketType, StockQuoteResponse, EtfClassificationResult } from '../../types';
import { StockMarket } from '../../types';
import { StaleQuoteIndicator, EtfTypeBadge, Skeleton } from '../common';
import type { EtfType } from '../common';

interface PositionCardProps {
  position: StockPosition;
  baseCurrency?: string;
  homeCurrency?: string;
  onPriceUpdate?: (ticker: string, price: number, exchangeRate: number) => void;
  autoFetch?: boolean;
  refreshTrigger?: number;
}

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

const MARKET_LABELS: Record<number, string> = {
  [StockMarket.TW]: '台股',
  [StockMarket.US]: '美股',
  [StockMarket.UK]: '英股',
  4: 'Euronext',
};

// Get base currency for a market (TW -> TWD, US/UK -> USD, Euronext -> EUR as default, actual from quote)
const EURONEXT_MARKET: StockMarketType = 4 as StockMarketType;

const getMarketCurrency = (market: StockMarketType): string => {
  if (market === EURONEXT_MARKET) {
    return 'EUR';
  }
  return market === StockMarket.TW ? 'TWD' : 'USD';
};

// Cache key for localStorage - includes market to prevent cross-market conflicts
const getQuoteCacheKey = (ticker: string, market?: StockMarketType) =>
  `quote_cache_${ticker}_${market ?? 'default'}`;

interface CachedQuote {
  quote: StockQuoteResponse;
  updatedAt: string;
  market: StockMarketType;
  fromCache?: boolean;
  isStale?: boolean;
}

export function PositionCard({
  position,
  homeCurrency = 'TWD',
  onPriceUpdate,
  autoFetch = true,
  refreshTrigger,
}: PositionCardProps) {
  // Load cached data on init
  const loadCachedQuote = (): { quote: StockQuoteResponse | null; updatedAt: Date | null; market: StockMarketType } => {
    // 優先使用 position.market（從後端交易資料取得）
    const positionMarket = position.market ?? guessMarket(position.ticker);

    try {
      const cached = localStorage.getItem(getQuoteCacheKey(position.ticker, positionMarket));
      if (cached) {
        const data: CachedQuote = JSON.parse(cached);
        return {
          quote: data.quote,
          updatedAt: new Date(data.updatedAt),
          // 使用 position.market 覆蓋快取中的市場（使用者可能已修改交易的市場）
          market: positionMarket,
        };
      }
    } catch {
      // Ignore cache errors
    }
    return { quote: null, updatedAt: null, market: positionMarket };
  };

  const cachedData = loadCachedQuote();
  const [selectedMarket, setSelectedMarket] = useState<StockMarketType>(cachedData.market);
  const [fetchStatus, setFetchStatus] = useState<'idle' | 'loading' | 'success' | 'error'>(
    cachedData.quote ? 'success' : 'idle'
  );
  const [lastQuote, setLastQuote] = useState<StockQuoteResponse | null>(cachedData.quote);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(cachedData.updatedAt);
  const [error, setError] = useState<string | null>(null);
  const [isFromCache, setIsFromCache] = useState(false);
  const [isStaleQuote, setIsStaleQuote] = useState(false);
  const [etfClassification, setEtfClassification] = useState<EtfClassificationResult | null>(null);
  const hasFetched = useRef(false);

  // Re-read from cache when refreshTrigger changes (parent finished fetching)
  useEffect(() => {
    if (!refreshTrigger) {
      return;
    }

    const freshData = loadCachedQuote();
    if (freshData.quote) {
      setLastQuote(freshData.quote);
      setLastUpdated(freshData.updatedAt);
      setSelectedMarket(freshData.market);
      setFetchStatus('success');
    }
  }, [refreshTrigger]);

  // Save to cache when quote updates
  const saveToCache = (quote: StockQuoteResponse, market: StockMarketType, fromCache?: boolean) => {
    try {
      const data: CachedQuote = {
        quote,
        updatedAt: new Date().toISOString(),
        market,
        fromCache,
      };
      localStorage.setItem(getQuoteCacheKey(position.ticker, market), JSON.stringify(data));
    } catch {
      // Ignore cache errors
    }
  };

  const formatNumber = (value: number | null | undefined, decimals = 2) => {
    if (value == null) return '-';
    return value.toLocaleString('zh-TW', {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    });
  };

  // Format home currency (TWD = integer, others = 2 decimals)
  const formatHomeCurrency = (value: number | null | undefined) => {
    if (value == null) return '-';
    if (homeCurrency === 'TWD') {
      return Math.round(value).toLocaleString('zh-TW');
    }
    return formatNumber(value, 2);
  };

  const formatPercent = (value: number | null | undefined) => {
    if (value == null) return '-';
    const sign = value >= 0 ? '+' : '';
    return `${sign}${formatNumber(value, 2)}%`;
  };

  const formatTime = (date: Date) => {
    return date.toLocaleTimeString('zh-TW', { hour: '2-digit', minute: '2-digit' });
  };

  const handleFetchQuote = useCallback(async (market?: StockMarketType) => {
    const targetMarket = market ?? selectedMarket;
    setFetchStatus('loading');
    setError(null);
    setIsFromCache(false);
    setIsStaleQuote(false);

    try {
      // Use Euronext API if position's market is EU (4)
      const positionMarket = position.market ?? targetMarket;
      if (positionMarket === EURONEXT_MARKET) {
        const euronextQuote = await marketDataApi.getEuronextQuoteByTicker(
          position.ticker,
          homeCurrency
        );
        if (euronextQuote) {
          const syntheticQuote: StockQuoteResponse = {
            symbol: position.ticker,
            name: euronextQuote.name || position.ticker,
            price: euronextQuote.price,
            change: euronextQuote.change ?? undefined,
            changePercent: euronextQuote.changePercent ?? undefined,
            market: EURONEXT_MARKET,
            source: 'Euronext',
            fetchedAt: euronextQuote.marketTime || new Date().toISOString(),
            exchangeRate: euronextQuote.exchangeRate ?? undefined,
            exchangeRatePair: `${euronextQuote.currency}/${homeCurrency}`,
          };
          setLastQuote(syntheticQuote);
          setLastUpdated(new Date());
          setFetchStatus('success');
          setSelectedMarket(EURONEXT_MARKET);
          setIsFromCache(euronextQuote.fromCache);
          saveToCache(syntheticQuote, EURONEXT_MARKET, euronextQuote.fromCache);

          if (onPriceUpdate && euronextQuote.exchangeRate) {
            onPriceUpdate(position.ticker, euronextQuote.price, euronextQuote.exchangeRate);
          }
          return;
        }
      }

      const quote = await stockPriceApi.getQuoteWithRate(targetMarket, position.ticker, homeCurrency);

      // US14: No market fallback - strictly use position's market
      if (quote) {
        setLastQuote(quote);
        setLastUpdated(new Date());
        setFetchStatus('success');
        saveToCache(quote, targetMarket);

        // Notify parent with the fetched price and exchange rate
        if (onPriceUpdate && quote.exchangeRate) {
          onPriceUpdate(position.ticker, quote.price, quote.exchangeRate);
        }
      } else {
        setFetchStatus('error');
        setError('無報價');
      }
    } catch (err) {
      // Check for Euronext market
      const positionMarket = position.market ?? selectedMarket;
      if (positionMarket === EURONEXT_MARKET) {
        setFetchStatus('error');
        setError('Euronext 報價獲取失敗');
        return;
      }

      // US14: No market fallback - show error directly
      setFetchStatus('error');
      setError(err instanceof Error ? err.message : '無報價');
    }
  }, [homeCurrency, onPriceUpdate, position.market, position.ticker, selectedMarket]);

  // Auto-fetch on mount (always fetch fresh, use cache only for initial display)
  useEffect(() => {
    if (!autoFetch || hasFetched.current) {
      return;
    }

    hasFetched.current = true;

    // Always fetch fresh quote on mount
    handleFetchQuote();

    // Fetch ETF classification
    etfClassificationApi
      .getClassification(position.ticker)
      .then(setEtfClassification)
      .catch(() => {
        /* ignore classification errors */
      });

    // Also notify parent with cached data immediately if available
    if (lastQuote && onPriceUpdate && lastQuote.exchangeRate) {
      onPriceUpdate(position.ticker, lastQuote.price, lastQuote.exchangeRate);
    }
  }, [autoFetch, handleFetchQuote, lastQuote, onPriceUpdate, position.ticker]);

  const hasValuation = position.currentValueHome != null;

  const pnlColor = hasValuation && (position.unrealizedPnlHome ?? 0) >= 0
    ? 'number-positive'
    : hasValuation
      ? 'number-negative'
      : '';

  // 優先使用 position.currency（從後端交易資料取得），否則根據市場推測
  const marketCurrency = position.currency ?? getMarketCurrency(selectedMarket);
  const hasCurrentValue = position.currentValueHome !== undefined && position.currentValueHome !== null;

  return (
    <div className="card-dark p-5 hover:border-[var(--border-hover)] transition-all">
      {/* Header: Ticker + Quote */}
      <div className="flex justify-between items-start mb-4">
        <div>
          <div className="flex items-center gap-2">
            <h3 className="text-lg font-bold text-[var(--accent-cream)]">{position.ticker}</h3>
            <span className="text-xs text-[var(--text-muted)] bg-[var(--bg-tertiary)] px-1.5 py-0.5 rounded">
              {MARKET_LABELS[selectedMarket]}
            </span>
          </div>
          <span className="text-sm text-[var(--text-muted)] number-display">
            {formatNumber(position.totalShares, 4)} 股
          </span>
          {etfClassification && etfClassification.type !== 'Unknown' && (
            <EtfTypeBadge
              type={etfClassification.type as EtfType}
              isConfirmed={etfClassification.isConfirmed}
              className="mt-1"
            />
          )}
        </div>

        {/* Quote display */}
        <div className="text-right">
          {lastQuote ? (
            <>
              <div className="text-xl font-bold text-[var(--text-primary)] number-display">
                {formatNumber(lastQuote.price)} <span className="text-sm font-normal text-[var(--text-muted)]">{marketCurrency}</span>
              </div>
              <div className="flex items-center justify-end gap-2">
                {lastQuote.changePercent && (
                  <span className={`text-sm font-medium ${lastQuote.changePercent.startsWith('-') ? 'number-negative' : 'number-positive'}`}>
                    {lastQuote.changePercent}
                  </span>
                )}
                {lastUpdated && (
                  <span className="text-xs text-[var(--text-muted)]">
                    {formatTime(lastUpdated)}
                  </span>
                )}
              </div>
              {isFromCache && (
                <StaleQuoteIndicator
                  isStale={isStaleQuote}
                  fromCache={isFromCache}
                  fetchedAt={lastUpdated?.toISOString()}
                />
              )}
            </>
          ) : fetchStatus === 'loading' ? (
            <div className="flex flex-col items-end gap-1">
              <Skeleton width="w-24" height="h-7" />
              <Skeleton width="w-16" height="h-5" />
            </div>
          ) : (
            <div className="text-xl text-[var(--text-muted)]"></div>
          )}
          {fetchStatus === 'error' && (
            <div className="text-xs text-[var(--color-danger)]">{error}</div>
          )}
        </div>
      </div>

      <div className="space-y-3 text-base">
        <div className="flex justify-between">
          <span className="text-[var(--text-muted)]">單位成本:</span>
          <span className="font-medium text-[var(--text-primary)] number-display">
            {formatNumber(position.averageCostPerShareSource)} {marketCurrency}
          </span>
        </div>

        <div className="flex justify-between">
          <span className="text-[var(--text-muted)]">總成本:</span>
          <span className="font-medium text-[var(--text-primary)] number-display">
            {position.totalCostHome != null
              ? `${formatHomeCurrency(position.totalCostHome)} ${homeCurrency}`
              : `${formatNumber(position.totalCostSource)} ${marketCurrency}`}
          </span>
        </div>

        <hr className="border-[var(--border-color)] my-3" />

        <div className="flex justify-between">
          <span className="text-[var(--text-muted)]">現值:</span>
          <span className="font-medium text-[var(--text-primary)] number-display">
            {hasCurrentValue ? `${formatHomeCurrency(position.currentValueHome)} ${homeCurrency}` : ''}
          </span>
        </div>

        <div className="flex justify-between">
          <span className="text-[var(--text-muted)]">未實現損益:</span>
          <span className={`font-medium number-display ${hasCurrentValue ? pnlColor : ''}`}>
            {hasCurrentValue ? (
              <>
                {formatHomeCurrency(position.unrealizedPnlHome ?? 0)} {homeCurrency}
                {position.unrealizedPnlPercentage !== undefined && (
                  <span className="ml-1 text-sm">
                    ({formatPercent(position.unrealizedPnlPercentage)})
                  </span>
                )}
              </>
            ) : ''}
          </span>
        </div>
      </div>
    </div>
  );
}
