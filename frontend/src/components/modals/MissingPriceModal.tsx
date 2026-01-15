import { useState } from 'react';
import { X, AlertCircle } from 'lucide-react';
import type { MissingPrice, YearEndPriceInfo } from '../../types';

interface MissingPriceModalProps {
  isOpen: boolean;
  onClose: () => void;
  missingPrices: MissingPrice[];
  year: number;
  onSubmit: (prices: Record<string, YearEndPriceInfo>) => void;
}

export function MissingPriceModal({
  isOpen,
  onClose,
  missingPrices,
  year,
  onSubmit,
}: MissingPriceModalProps) {
  const [priceInputs, setPriceInputs] = useState<Record<string, { price: string; exchangeRate: string }>>({});

  if (!isOpen) return null;

  const handleInputChange = (ticker: string, field: 'price' | 'exchangeRate', value: string) => {
    setPriceInputs(prev => ({
      ...prev,
      [ticker]: {
        ...prev[ticker],
        [field]: value,
      },
    }));
  };

  const handleSubmit = () => {
    const prices: Record<string, YearEndPriceInfo> = {};

    for (const missing of missingPrices) {
      const input = priceInputs[missing.ticker];
      if (input?.price && input?.exchangeRate) {
        prices[missing.ticker] = {
          price: parseFloat(input.price),
          exchangeRate: parseFloat(input.exchangeRate),
        };
      }
    }

    onSubmit(prices);
    onClose();
  };

  const isValid = missingPrices.every(mp => {
    const input = priceInputs[mp.ticker];
    return input?.price && input?.exchangeRate &&
      !isNaN(parseFloat(input.price)) &&
      !isNaN(parseFloat(input.exchangeRate));
  });

  const formatDate = (dateStr: string) => {
    const date = new Date(dateStr);
    return date.toLocaleDateString('zh-TW', {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  };

  return (
    <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50">
      <div className="card-dark p-0 w-full max-w-lg max-h-[80vh] overflow-y-auto m-4">
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-[var(--border-color)]">
          <div className="flex items-center gap-2">
            <AlertCircle className="w-5 h-5 text-[var(--color-warning)]" />
            <h2 className="text-lg font-bold text-[var(--text-primary)]">
              缺少 {year} 年度價格
            </h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="p-1 text-[var(--text-muted)] hover:text-[var(--text-primary)] rounded transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content */}
        <div className="p-5">
          <p className="text-[var(--text-secondary)] text-sm mb-4">
            為計算 {year} 年度績效，請輸入以下股票在指定日期的收盤價和匯率：
          </p>

          <div className="space-y-4">
            {missingPrices.map((mp) => (
              <div key={mp.ticker} className="p-4 bg-[var(--bg-tertiary)] rounded-lg">
                <div className="flex items-center justify-between mb-3">
                  <span className="font-medium text-[var(--accent-cream)]">{mp.ticker}</span>
                  <span className="text-xs text-[var(--text-muted)]">
                    {mp.priceType === 'YearEnd' ? '年底' : '年初基準'} ({formatDate(mp.date)})
                  </span>
                </div>
                <div className="grid grid-cols-2 gap-3">
                  <div>
                    <label className="block text-xs text-[var(--text-muted)] mb-1">
                      收盤價
                    </label>
                    <input
                      type="number"
                      step="0.01"
                      placeholder="0.00"
                      value={priceInputs[mp.ticker]?.price ?? ''}
                      onChange={(e) => handleInputChange(mp.ticker, 'price', e.target.value)}
                      className="w-full bg-[var(--bg-secondary)] border border-[var(--border-color)] rounded px-3 py-2 text-[var(--text-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-peach)]"
                    />
                  </div>
                  <div>
                    <label className="block text-xs text-[var(--text-muted)] mb-1">
                      匯率 (→TWD)
                    </label>
                    <input
                      type="number"
                      step="0.0001"
                      placeholder="32.00"
                      value={priceInputs[mp.ticker]?.exchangeRate ?? ''}
                      onChange={(e) => handleInputChange(mp.ticker, 'exchangeRate', e.target.value)}
                      className="w-full bg-[var(--bg-secondary)] border border-[var(--border-color)] rounded px-3 py-2 text-[var(--text-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-peach)]"
                    />
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>

        {/* Footer */}
        <div className="flex justify-end gap-3 px-5 py-4 border-t border-[var(--border-color)]">
          <button
            type="button"
            onClick={onClose}
            className="btn-dark px-4 py-2"
          >
            取消
          </button>
          <button
            type="button"
            onClick={handleSubmit}
            disabled={!isValid}
            className="btn-accent px-4 py-2 disabled:opacity-50"
          >
            計算績效
          </button>
        </div>
      </div>
    </div>
  );
}
