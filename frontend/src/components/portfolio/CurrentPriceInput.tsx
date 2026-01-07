import { useState, useEffect } from 'react';
import { RefreshCw, Loader2 } from 'lucide-react';
import { stockPriceApi } from '../../services/api';
import type { StockPosition, CurrentPriceInfo, StockMarket as StockMarketType, StockQuoteResponse } from '../../types';
import { StockMarket } from '../../types';

interface CurrentPriceInputProps {
  positions: StockPosition[];
  onPricesChange: (prices: Record<string, CurrentPriceInfo>) => void;
  baseCurrency?: string;
  homeCurrency?: string;
}

interface PriceEntry {
  ticker: string;
  price: string;
  exchangeRate: string;
  market: StockMarketType;
  fetchStatus: 'idle' | 'loading' | 'success' | 'error';
  lastQuote?: StockQuoteResponse;
  error?: string;
}

const marketLabels: Record<StockMarketType, string> = {
  [StockMarket.TW]: '台股',
  [StockMarket.US]: '美股',
  [StockMarket.UK]: '英股',
};

export function CurrentPriceInput({
  positions,
  onPricesChange,
  baseCurrency = 'USD',
}: CurrentPriceInputProps) {
  const [priceEntries, setPriceEntries] = useState<PriceEntry[]>([]);
  const [isExpanded, setIsExpanded] = useState(false);

  useEffect(() => {
    const entries = positions.map((pos) => ({
      ticker: pos.ticker,
      price: pos.currentPrice?.toString() || '',
      exchangeRate: pos.currentExchangeRate?.toString() || '',
      market: guessMarket(pos.ticker) as StockMarketType,
      fetchStatus: 'idle' as const,
    }));
    setPriceEntries(entries);
  }, [positions]);

  const guessMarket = (ticker: string): StockMarketType => {
    // If ticker is all digits or digits with letters at end (e.g., 2330, 00878), it's likely TW
    if (/^\d+[A-Za-z]?$/.test(ticker)) {
      return StockMarket.TW;
    }
    // If ticker ends with .L or has UK-style symbols
    if (ticker.endsWith('.L') || /^[A-Z]{2,4}$/.test(ticker) && ticker.length <= 4) {
      // Could be UK or US - default to US for short symbols
      return StockMarket.US;
    }
    return StockMarket.US;
  };

  const handlePriceChange = (ticker: string, field: 'price' | 'exchangeRate' | 'market', value: string | StockMarketType) => {
    setPriceEntries((prev) =>
      prev.map((entry) =>
        entry.ticker === ticker ? { ...entry, [field]: value, fetchStatus: 'idle' as const } : entry
      )
    );
  };

  const handleFetchQuote = async (ticker: string) => {
    const entry = priceEntries.find((e) => e.ticker === ticker);
    if (!entry) return;

    setPriceEntries((prev) =>
      prev.map((e) =>
        e.ticker === ticker ? { ...e, fetchStatus: 'loading', error: undefined } : e
      )
    );

    try {
      const quote = await stockPriceApi.getQuote(entry.market, ticker);
      setPriceEntries((prev) =>
        prev.map((e) =>
          e.ticker === ticker
            ? {
                ...e,
                price: quote.price.toString(),
                fetchStatus: 'success',
                lastQuote: quote,
                error: undefined,
              }
            : e
        )
      );
    } catch (err) {
      setPriceEntries((prev) =>
        prev.map((e) =>
          e.ticker === ticker
            ? {
                ...e,
                fetchStatus: 'error',
                error: err instanceof Error ? err.message : '獲取失敗',
              }
            : e
        )
      );
    }
  };

  const handleFetchAll = async () => {
    for (const entry of priceEntries) {
      await handleFetchQuote(entry.ticker);
    }
  };

  const handleApply = () => {
    const prices: Record<string, CurrentPriceInfo> = {};

    priceEntries.forEach((entry) => {
      const price = parseFloat(entry.price);
      const exchangeRate = parseFloat(entry.exchangeRate);

      if (!isNaN(price) && price > 0 && !isNaN(exchangeRate) && exchangeRate > 0) {
        prices[entry.ticker] = { price, exchangeRate };
      }
    });

    if (Object.keys(prices).length > 0) {
      onPricesChange(prices);
    }
  };

  const handleClear = () => {
    setPriceEntries((prev) =>
      prev.map((entry) => ({ ...entry, price: '', exchangeRate: '', fetchStatus: 'idle', lastQuote: undefined, error: undefined }))
    );
    onPricesChange({});
  };

  const filledCount = priceEntries.filter(
    (e) => e.price && e.exchangeRate && parseFloat(e.price) > 0 && parseFloat(e.exchangeRate) > 0
  ).length;

  const isAnyLoading = priceEntries.some((e) => e.fetchStatus === 'loading');

  return (
    <div className="card-dark p-5">
      <button
        type="button"
        onClick={() => setIsExpanded(!isExpanded)}
        className="flex items-center justify-between w-full text-left"
      >
        <div>
          <h3 className="text-lg font-bold text-[var(--text-primary)]">即時價格</h3>
          <p className="text-base text-[var(--text-muted)]">
            已輸入 {filledCount} / {positions.length} 筆價格
          </p>
        </div>
        <svg
          className={`w-5 h-5 text-[var(--text-muted)] transform transition-transform ${
            isExpanded ? 'rotate-180' : ''
          }`}
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
        >
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
        </svg>
      </button>

      {isExpanded && (
        <div className="mt-5 space-y-4">
          {/* Fetch All Button */}
          <div className="flex justify-end">
            <button
              type="button"
              onClick={handleFetchAll}
              disabled={isAnyLoading}
              className="btn-dark flex items-center gap-2 px-4 py-2 text-sm disabled:opacity-50"
            >
              {isAnyLoading ? (
                <Loader2 className="w-4 h-4 animate-spin" />
              ) : (
                <RefreshCw className="w-4 h-4" />
              )}
              獲取全部報價
            </button>
          </div>

          {/* Header */}
          <div className="grid grid-cols-12 gap-2 text-sm font-medium text-[var(--text-muted)] pb-3 border-b border-[var(--border-color)]">
            <div className="col-span-2">代號</div>
            <div className="col-span-2">市場</div>
            <div className="col-span-3">價格 ({baseCurrency})</div>
            <div className="col-span-3">匯率</div>
            <div className="col-span-2 text-center">獲取</div>
          </div>

          {priceEntries.map((entry) => (
            <div key={entry.ticker} className="space-y-1">
              <div className="grid grid-cols-12 gap-2 items-center">
                <div className="col-span-2 font-medium text-[var(--accent-cream)]">{entry.ticker}</div>
                <div className="col-span-2">
                  <select
                    value={entry.market}
                    onChange={(e) => handlePriceChange(entry.ticker, 'market', Number(e.target.value) as StockMarketType)}
                    className="input-dark w-full text-sm py-1.5"
                  >
                    <option value={StockMarket.TW}>{marketLabels[StockMarket.TW]}</option>
                    <option value={StockMarket.US}>{marketLabels[StockMarket.US]}</option>
                    <option value={StockMarket.UK}>{marketLabels[StockMarket.UK]}</option>
                  </select>
                </div>
                <div className="col-span-3">
                  <input
                    type="number"
                    step="0.01"
                    min="0"
                    value={entry.price}
                    onChange={(e) => handlePriceChange(entry.ticker, 'price', e.target.value)}
                    placeholder="0.00"
                    className="input-dark w-full text-sm py-1.5"
                  />
                </div>
                <div className="col-span-3">
                  <input
                    type="number"
                    step="0.0001"
                    min="0"
                    value={entry.exchangeRate}
                    onChange={(e) => handlePriceChange(entry.ticker, 'exchangeRate', e.target.value)}
                    placeholder="0.0000"
                    className="input-dark w-full text-sm py-1.5"
                  />
                </div>
                <div className="col-span-2 flex justify-center">
                  <button
                    type="button"
                    onClick={() => handleFetchQuote(entry.ticker)}
                    disabled={entry.fetchStatus === 'loading'}
                    className="p-1.5 text-[var(--text-muted)] hover:text-[var(--accent-peach)] hover:bg-[var(--bg-hover)] rounded transition-colors disabled:opacity-50"
                    title="獲取報價"
                  >
                    {entry.fetchStatus === 'loading' ? (
                      <Loader2 className="w-4 h-4 animate-spin" />
                    ) : (
                      <RefreshCw className="w-4 h-4" />
                    )}
                  </button>
                </div>
              </div>

              {/* Status row */}
              {entry.fetchStatus === 'success' && entry.lastQuote && (
                <div className="ml-2 text-xs text-[var(--color-success)]">
                  ✓ {entry.lastQuote.name} - {entry.lastQuote.source}
                  {entry.lastQuote.changePercent && ` (${entry.lastQuote.changePercent})`}
                </div>
              )}
              {entry.fetchStatus === 'error' && (
                <div className="ml-2 text-xs text-[var(--color-danger)]">
                  ✗ {entry.error || '獲取失敗'}
                </div>
              )}
            </div>
          ))}

          <div className="flex gap-3 pt-4 border-t border-[var(--border-color)]">
            <button
              type="button"
              onClick={handleApply}
              disabled={filledCount === 0}
              className="btn-accent px-5 py-2 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              套用價格
            </button>
            <button
              type="button"
              onClick={handleClear}
              className="btn-dark px-5 py-2"
            >
              清除全部
            </button>
          </div>

          <p className="text-sm text-[var(--text-muted)]">
            選擇市場並點擊獲取按鈕取得即時報價，匯率需手動輸入。台股延遲約 20 秒，美/英股為即時。
          </p>
        </div>
      )}
    </div>
  );
}
