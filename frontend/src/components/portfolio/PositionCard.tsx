import { useState, useEffect, useRef } from 'react';
import { Settings2 } from 'lucide-react';
import { stockPriceApi } from '../../services/api';
import type { StockPosition, StockMarket as StockMarketType, StockQuoteResponse } from '../../types';
import { StockMarket } from '../../types';

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

const MARKET_LABELS: Record<StockMarketType, string> = {
  [StockMarket.TW]: '台股',
  [StockMarket.US]: '美股',
  [StockMarket.UK]: '英股',
};

// Cache key for localStorage
const getQuoteCacheKey = (ticker: string) => `quote_cache_${ticker}`;

interface CachedQuote {
  quote: StockQuoteResponse;
  updatedAt: string;
  market: StockMarketType;
}

export function PositionCard({
  position,
  baseCurrency = 'USD',
  homeCurrency = 'TWD',
  onPriceUpdate,
  autoFetch = true,
  refreshTrigger,
}: PositionCardProps) {
  // Load cached data on init
  const loadCachedQuote = (): { quote: StockQuoteResponse | null; updatedAt: Date | null; market: StockMarketType } => {
    try {
      const cached = localStorage.getItem(getQuoteCacheKey(position.ticker));
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
    return { quote: null, updatedAt: null, market: guessMarket(position.ticker) };
  };

  const cachedData = loadCachedQuote();
  const [selectedMarket, setSelectedMarket] = useState<StockMarketType>(cachedData.market);
  const [fetchStatus, setFetchStatus] = useState<'idle' | 'loading' | 'success' | 'error'>(
    cachedData.quote ? 'success' : 'idle'
  );
  const [lastQuote, setLastQuote] = useState<StockQuoteResponse | null>(cachedData.quote);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(cachedData.updatedAt);
  const [error, setError] = useState<string | null>(null);
  const [showMarketSelector, setShowMarketSelector] = useState(false);
  const hasFetched = useRef(false);

  // Re-read from cache when refreshTrigger changes (parent finished fetching)
  useEffect(() => {
    if (refreshTrigger) {
      const freshData = loadCachedQuote();
      if (freshData.quote) {
        setLastQuote(freshData.quote);
        setLastUpdated(freshData.updatedAt);
        setSelectedMarket(freshData.market);
        setFetchStatus('success');
      }
    }
  }, [refreshTrigger]);

  // Auto-fetch on mount (only if no cache or cache is old)
  useEffect(() => {
    if (autoFetch && !hasFetched.current) {
      hasFetched.current = true;
      // If cache is older than 5 minutes, refresh
      const cacheAge = lastUpdated ? Date.now() - lastUpdated.getTime() : Infinity;
      if (cacheAge > 5 * 60 * 1000) {
        handleFetchQuote();
      } else if (lastQuote && onPriceUpdate && lastQuote.exchangeRate) {
        // Notify parent with cached data
        onPriceUpdate(position.ticker, lastQuote.price, lastQuote.exchangeRate);
      }
    }
  }, []);

  // Save to cache when quote updates
  const saveToCache = (quote: StockQuoteResponse, market: StockMarketType) => {
    try {
      const data: CachedQuote = {
        quote,
        updatedAt: new Date().toISOString(),
        market,
      };
      localStorage.setItem(getQuoteCacheKey(position.ticker), JSON.stringify(data));
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

  const handleFetchQuote = async (market?: StockMarketType) => {
    const targetMarket = market ?? selectedMarket;
    setFetchStatus('loading');
    setError(null);

    try {
      let quote = await stockPriceApi.getQuoteWithRate(targetMarket, position.ticker, homeCurrency);
      let finalMarket = targetMarket;

      // If US market fails, try UK market (for ETFs like VWRA that are listed on LSE)
      if (!quote && targetMarket === StockMarket.US) {
        quote = await stockPriceApi.getQuoteWithRate(StockMarket.UK, position.ticker, homeCurrency);
        if (quote) {
          finalMarket = StockMarket.UK;
          setSelectedMarket(StockMarket.UK);
        }
      }

      if (quote) {
        setLastQuote(quote);
        setLastUpdated(new Date());
        setFetchStatus('success');
        saveToCache(quote, finalMarket);

        // Notify parent with the fetched price and exchange rate
        if (onPriceUpdate && quote.exchangeRate) {
          onPriceUpdate(position.ticker, quote.price, quote.exchangeRate);
        }
      } else {
        setFetchStatus('error');
        setError('找不到報價');
      }
    } catch (err) {
      // If US fails with exception, try UK as fallback
      if (targetMarket === StockMarket.US) {
        try {
          const ukQuote = await stockPriceApi.getQuoteWithRate(StockMarket.UK, position.ticker, homeCurrency);
          if (ukQuote) {
            setSelectedMarket(StockMarket.UK);
            setLastQuote(ukQuote);
            setLastUpdated(new Date());
            setFetchStatus('success');
            saveToCache(ukQuote, StockMarket.UK);
            if (onPriceUpdate && ukQuote.exchangeRate) {
              onPriceUpdate(position.ticker, ukQuote.price, ukQuote.exchangeRate);
            }
            return;
          }
        } catch {
          // UK also failed
        }
      }
      setFetchStatus('error');
      setError(err instanceof Error ? err.message : '獲取失敗');
    }
  };

  const handleMarketChange = (market: StockMarketType) => {
    setSelectedMarket(market);
    setShowMarketSelector(false);
    handleFetchQuote(market);
  };

  const pnlColor = (position.unrealizedPnlHome ?? 0) >= 0
    ? 'number-positive'
    : 'number-negative';

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
            <button
              type="button"
              onClick={(e) => { e.preventDefault(); e.stopPropagation(); setShowMarketSelector(!showMarketSelector); }}
              className="p-1 text-[var(--text-muted)] hover:text-[var(--accent-butter)] hover:bg-[var(--bg-hover)] rounded transition-colors opacity-40 hover:opacity-100"
              title="切換市場"
            >
              <Settings2 className="w-3.5 h-3.5" />
            </button>
          </div>
          <span className="text-sm text-[var(--text-muted)] number-display">
            {formatNumber(position.totalShares, 4)} 股
          </span>
        </div>

        {/* Quote display */}
        <div className="text-right">
          {lastQuote ? (
            <>
              <div className="text-xl font-bold text-[var(--text-primary)] number-display">
                {formatNumber(lastQuote.price)} <span className="text-sm font-normal text-[var(--text-muted)]">{baseCurrency}</span>
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
            </>
          ) : (
            <div className="text-xl text-[var(--text-muted)]">-</div>
          )}
          {fetchStatus === 'error' && (
            <div className="text-xs text-[var(--color-danger)]">{error}</div>
          )}
        </div>
      </div>

      {/* Market selector popup */}
      {showMarketSelector && (
        <div className="mb-3 p-2 rounded bg-[var(--bg-tertiary)] border border-[var(--border-color)]">
          <div className="text-xs text-[var(--text-muted)] mb-2">選擇市場：</div>
          <div className="flex gap-2">
            {Object.entries(MARKET_LABELS).map(([value, label]) => (
              <button
                key={value}
                type="button"
                onClick={() => handleMarketChange(Number(value) as StockMarketType)}
                className={`px-3 py-1 text-sm rounded transition-colors ${
                  selectedMarket === Number(value)
                    ? 'bg-[var(--accent-peach)] text-[var(--bg-primary)]'
                    : 'bg-[var(--bg-hover)] text-[var(--text-secondary)] hover:bg-[var(--bg-active)]'
                }`}
              >
                {label}
              </button>
            ))}
          </div>
        </div>
      )}

      <div className="space-y-3 text-base">
        <div className="flex justify-between">
          <span className="text-[var(--text-muted)]">單位成本:</span>
          <span className="font-medium text-[var(--text-primary)] number-display">
            {formatNumber(position.averageCostPerShareSource)} {baseCurrency}
          </span>
        </div>

        <div className="flex justify-between">
          <span className="text-[var(--text-muted)]">總成本:</span>
          <span className="font-medium text-[var(--text-primary)] number-display">
            {formatHomeCurrency(position.totalCostHome)} {homeCurrency}
          </span>
        </div>

        <hr className="border-[var(--border-color)] my-3" />

        <div className="flex justify-between">
          <span className="text-[var(--text-muted)]">現值:</span>
          <span className="font-medium text-[var(--text-primary)] number-display">
            {hasCurrentValue ? `${formatHomeCurrency(position.currentValueHome)} ${homeCurrency}` : '-'}
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
            ) : '-'}
          </span>
        </div>
      </div>
    </div>
  );
}
