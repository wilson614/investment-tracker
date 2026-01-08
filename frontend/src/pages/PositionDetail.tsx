import { useState, useEffect, useCallback } from 'react';
import { useParams, Link } from 'react-router-dom';
import { ArrowLeft, RefreshCw, Loader2 } from 'lucide-react';
import { portfolioApi, transactionApi, stockPriceApi } from '../services/api';
import { TransactionList } from '../components/transactions/TransactionList';
import { StockMarket } from '../types';
import type {
  Portfolio,
  StockPosition,
  StockTransaction,
  StockMarket as StockMarketType,
  StockQuoteResponse,
} from '../types';

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
};

// Cache key for localStorage (same as PositionCard)
const getQuoteCacheKey = (ticker: string) => `quote_cache_${ticker}`;

interface CachedQuote {
  quote: StockQuoteResponse;
  updatedAt: string;
  market: StockMarketType;
}

// Load cached market from localStorage
const loadCachedMarket = (ticker: string): StockMarketType => {
  try {
    const cached = localStorage.getItem(getQuoteCacheKey(ticker));
    if (cached) {
      const data: CachedQuote = JSON.parse(cached);
      return data.market;
    }
  } catch {
    // Ignore cache errors
  }
  return guessMarket(ticker);
};

export function PositionDetailPage() {
  const { id: portfolioId, ticker } = useParams<{ id: string; ticker: string }>();
  const [portfolio, setPortfolio] = useState<Portfolio | null>(null);
  const [position, setPosition] = useState<StockPosition | null>(null);
  const [transactions, setTransactions] = useState<StockTransaction[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Quote state
  const [selectedMarket, setSelectedMarket] = useState<StockMarketType>(
    ticker ? loadCachedMarket(ticker) : StockMarket.US
  );
  const [fetchStatus, setFetchStatus] = useState<'idle' | 'loading' | 'success' | 'error'>('idle');
  const [lastQuote, setLastQuote] = useState<StockQuoteResponse | null>(null);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);

  const loadData = useCallback(async () => {
    if (!portfolioId || !ticker) return;

    try {
      setIsLoading(true);
      setError(null);

      const [summaryData, txData] = await Promise.all([
        portfolioApi.getSummary(portfolioId),
        transactionApi.getByPortfolio(portfolioId),
      ]);

      setPortfolio(summaryData.portfolio);

      // Find position for this ticker
      const pos = summaryData.positions.find(
        (p) => p.ticker.toUpperCase() === ticker.toUpperCase()
      );
      setPosition(pos || null);

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
  }, [portfolioId, ticker]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleFetchQuote = async () => {
    if (!portfolio) return;

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
        setLastQuote(quote);
        setLastUpdated(new Date());
        setFetchStatus('success');

        // Recalculate position with current price
        if (position && quote.exchangeRate) {
          const currentValue = position.totalShares * quote.price * quote.exchangeRate;
          const pnl = currentValue - position.totalCostHome;
          const pnlPct = position.totalCostHome > 0 ? (pnl / position.totalCostHome) * 100 : 0;

          setPosition({
            ...position,
            currentPrice: quote.price,
            currentExchangeRate: quote.exchangeRate,
            currentValueHome: currentValue,
            unrealizedPnlHome: pnl,
            unrealizedPnlPercentage: pnlPct,
          });
        }
      } else {
        setFetchStatus('error');
      }
    } catch {
      setFetchStatus('error');
    }
  };

  const handleDeleteTransaction = async (transactionId: string) => {
    if (!window.confirm('確定要刪除此交易紀錄嗎？')) return;
    await transactionApi.delete(transactionId);
    await loadData();
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
  const hasCurrentValue = position?.currentValueHome != null;
  const homeCurrency = portfolio.homeCurrency;
  const baseCurrency = portfolio.baseCurrency;

  return (
    <div className="min-h-screen py-8">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        {/* Back button */}
        <Link
          to={`/portfolio/${portfolioId}`}
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
            {fetchStatus === 'success' && lastQuote && (
              <span className="text-sm text-[var(--color-success)]">
                已更新
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
              <div className="metric-card metric-card-cream">
                <p className="text-sm text-[var(--text-muted)] mb-1">持股數量</p>
                <p className="text-xl font-bold text-[var(--accent-cream)] number-display">
                  {formatNumber(position.totalShares, 4)}
                </p>
                <p className="text-sm text-[var(--text-muted)]">股</p>
              </div>

              <div className="metric-card metric-card-sand">
                <p className="text-sm text-[var(--text-muted)] mb-1">單位成本</p>
                <p className="text-xl font-bold text-[var(--accent-sand)] number-display">
                  {formatNumber(position.averageCostPerShareSource)}
                </p>
                <p className="text-sm text-[var(--text-muted)]">{baseCurrency}/股</p>
              </div>

              <div className="metric-card metric-card-butter">
                <p className="text-sm text-[var(--text-muted)] mb-1">總成本</p>
                <p className="text-xl font-bold text-[var(--accent-butter)] number-display">
                  {formatTWD(position.totalCostHome)}
                </p>
                <p className="text-sm text-[var(--text-muted)]">{homeCurrency}</p>
              </div>

              {lastQuote && (
                <div className="metric-card metric-card-peach">
                  <p className="text-sm text-[var(--text-muted)] mb-1">現價</p>
                  <p className="text-xl font-bold text-[var(--accent-peach)] number-display">
                    {formatNumber(lastQuote.price)}
                  </p>
                  <p className="text-sm text-[var(--text-muted)]">
                    {baseCurrency} · 匯率 {formatNumber(lastQuote.exchangeRate ?? 0, 4)}
                  </p>
                </div>
              )}
            </div>

            {hasCurrentValue && (
              <>
                <hr className="border-[var(--border-color)] my-4" />
                <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
                  <div className="metric-card metric-card-sand">
                    <p className="text-sm text-[var(--text-muted)] mb-1">目前市值</p>
                    <p className="text-xl font-bold text-[var(--accent-sand)] number-display">
                      {formatTWD(position.currentValueHome)}
                    </p>
                    <p className="text-sm text-[var(--text-muted)]">{homeCurrency}</p>
                  </div>

                  <div className="metric-card metric-card-peach">
                    <p className="text-sm text-[var(--text-muted)] mb-1">未實現損益</p>
                    <p className={`text-xl font-bold number-display ${pnlColor}`}>
                      {formatTWD(position.unrealizedPnlHome)}
                    </p>
                    <p className={`text-sm ${pnlColor}`}>
                      {formatPercent(position.unrealizedPnlPercentage)}
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
          <div className="px-5 py-4 border-b border-[var(--border-color)]">
            <h2 className="text-lg font-bold text-[var(--text-primary)]">
              {ticker} 交易紀錄
            </h2>
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
