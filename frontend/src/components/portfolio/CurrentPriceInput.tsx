import { useState, useEffect } from 'react';
import type { StockPosition, CurrentPriceInfo } from '../../types';

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
}

export function CurrentPriceInput({
  positions,
  onPricesChange,
  baseCurrency = 'USD',
  homeCurrency = 'TWD',
}: CurrentPriceInputProps) {
  const [priceEntries, setPriceEntries] = useState<PriceEntry[]>([]);
  const [isExpanded, setIsExpanded] = useState(false);

  useEffect(() => {
    const entries = positions.map((pos) => ({
      ticker: pos.ticker,
      price: pos.currentPrice?.toString() || '',
      exchangeRate: pos.currentExchangeRate?.toString() || '',
    }));
    setPriceEntries(entries);
  }, [positions]);

  const handlePriceChange = (ticker: string, field: 'price' | 'exchangeRate', value: string) => {
    setPriceEntries((prev) =>
      prev.map((entry) =>
        entry.ticker === ticker ? { ...entry, [field]: value } : entry
      )
    );
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
      prev.map((entry) => ({ ...entry, price: '', exchangeRate: '' }))
    );
    onPricesChange({});
  };

  const filledCount = priceEntries.filter(
    (e) => e.price && e.exchangeRate && parseFloat(e.price) > 0 && parseFloat(e.exchangeRate) > 0
  ).length;

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
          <div className="grid grid-cols-12 gap-3 text-base font-medium text-[var(--text-muted)] pb-3 border-b border-[var(--border-color)]">
            <div className="col-span-3">股票代號</div>
            <div className="col-span-4">價格 ({baseCurrency})</div>
            <div className="col-span-5">匯率 ({baseCurrency}/{homeCurrency})</div>
          </div>

          {priceEntries.map((entry) => (
            <div key={entry.ticker} className="grid grid-cols-12 gap-3 items-center">
              <div className="col-span-3 font-medium text-[var(--accent-cream)]">{entry.ticker}</div>
              <div className="col-span-4">
                <input
                  type="number"
                  step="0.01"
                  min="0"
                  value={entry.price}
                  onChange={(e) => handlePriceChange(entry.ticker, 'price', e.target.value)}
                  placeholder="0.00"
                  className="input-dark w-full text-base py-2"
                />
              </div>
              <div className="col-span-5">
                <input
                  type="number"
                  step="0.0001"
                  min="0"
                  value={entry.exchangeRate}
                  onChange={(e) => handlePriceChange(entry.ticker, 'exchangeRate', e.target.value)}
                  placeholder="0.0000"
                  className="input-dark w-full text-base py-2"
                />
              </div>
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
            輸入即時股價與匯率以計算未實現損益與年化報酬率 (XIRR)。
          </p>
        </div>
      )}
    </div>
  );
}
