import { useState } from 'react';
import { X, AlertCircle, Loader2, CheckCircle } from 'lucide-react';
import { marketDataApi } from '../../services/api';
import type { MissingPrice } from '../../types';

interface ManualPriceEntryModalProps {
  isOpen: boolean;
  onClose: () => void;
  missingPrices: MissingPrice[];
  year: number;
  onSuccess: () => void;
}

interface PriceInput {
  price: string;
  currency: string;
}

type SaveStatus = 'idle' | 'saving' | 'saved' | 'error';

export function ManualPriceEntryModal({
  isOpen,
  onClose,
  missingPrices,
  year,
  onSuccess,
}: ManualPriceEntryModalProps) {
  const [priceInputs, setPriceInputs] = useState<Record<string, PriceInput>>({});
  const [saveStatuses, setSaveStatuses] = useState<Record<string, SaveStatus>>({});
  const [error, setError] = useState<string | null>(null);

  if (!isOpen) return null;

  const handlePriceChange = (ticker: string, field: keyof PriceInput, value: string) => {
    setPriceInputs(prev => ({
      ...prev,
      [ticker]: {
        ...prev[ticker],
        [field]: value,
      },
    }));
  };

  const handleSavePrice = async (ticker: string, date: string) => {
    const input = priceInputs[ticker];
    if (!input?.price) return;

    setSaveStatuses(prev => ({ ...prev, [ticker]: 'saving' }));
    setError(null);

    try {
      await marketDataApi.saveManualYearEndPrice({
        ticker,
        year,
        price: parseFloat(input.price),
        currency: input.currency || 'USD',
        actualDate: date,
      });
      setSaveStatuses(prev => ({ ...prev, [ticker]: 'saved' }));
    } catch (err) {
      setSaveStatuses(prev => ({ ...prev, [ticker]: 'error' }));
      setError(err instanceof Error ? err.message : 'Failed to save price');
    }
  };

  const allSaved = missingPrices.every(mp => saveStatuses[mp.ticker] === 'saved');

  const handleDone = () => {
    if (allSaved) {
      onSuccess();
    }
    onClose();
  };

  const formatDate = (dateStr: string) => {
    const date = new Date(dateStr);
    return date.toLocaleDateString('zh-TW', {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  };

  const renderSaveButton = (ticker: string, onClick: () => void, disabled: boolean) => {
    const status = saveStatuses[ticker];

    if (status === 'saving') {
      return (
        <button type="button" disabled className="btn-dark px-3 py-1 text-sm flex items-center gap-1">
          <Loader2 className="w-4 h-4 animate-spin" />
          儲存中...
        </button>
      );
    }

    if (status === 'saved') {
      return (
        <button type="button" disabled className="btn-dark px-3 py-1 text-sm flex items-center gap-1 text-green-400">
          <CheckCircle className="w-4 h-4" />
          已儲存
        </button>
      );
    }

    return (
      <button
        type="button"
        onClick={onClick}
        disabled={disabled}
        className="btn-accent px-3 py-1 text-sm disabled:opacity-50"
      >
        儲存到緩存
      </button>
    );
  };

  return (
    <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50">
      <div className="card-dark p-0 w-full max-w-lg max-h-[80vh] overflow-y-auto m-4">
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-[var(--border-color)]">
          <div className="flex items-center gap-2">
            <AlertCircle className="w-5 h-5 text-[var(--color-warning)]" />
            <h2 className="text-lg font-bold text-[var(--text-primary)]">
              手動輸入 {year} 年底價格
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
            無法自動取得以下股票的 {year} 年底價格。請手動輸入後儲存到緩存，供日後計算使用。
          </p>

          {error && (
            <div className="mb-4 p-3 bg-red-900/30 border border-red-700 rounded text-red-300 text-sm">
              {error}
            </div>
          )}

          <div className="space-y-4">
            {missingPrices.map((mp) => (
              <div key={mp.ticker} className="p-4 bg-[var(--bg-tertiary)] rounded-lg">
                <div className="flex items-center justify-between mb-3">
                  <span className="font-medium text-[var(--accent-cream)]">{mp.ticker}</span>
                  <span className="text-xs text-[var(--text-muted)]">
                    {mp.priceType === 'YearEnd' ? '年底' : '年初基準'} ({formatDate(mp.date)})
                  </span>
                </div>
                <div className="flex gap-3 items-end">
                  <div className="flex-1">
                    <label className="block text-xs text-[var(--text-muted)] mb-1">
                      收盤價
                    </label>
                    <input
                      type="number"
                      step="0.01"
                      placeholder="0.00"
                      value={priceInputs[mp.ticker]?.price ?? ''}
                      onChange={(e) => handlePriceChange(mp.ticker, 'price', e.target.value)}
                      disabled={saveStatuses[mp.ticker] === 'saved'}
                      className="w-full bg-[var(--bg-secondary)] border border-[var(--border-color)] rounded px-3 py-2 text-[var(--text-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-peach)] disabled:opacity-50"
                    />
                  </div>
                  <div className="w-20">
                    <label className="block text-xs text-[var(--text-muted)] mb-1">
                      幣別
                    </label>
                    <input
                      type="text"
                      placeholder="USD"
                      value={priceInputs[mp.ticker]?.currency ?? 'USD'}
                      onChange={(e) => handlePriceChange(mp.ticker, 'currency', e.target.value)}
                      disabled={saveStatuses[mp.ticker] === 'saved'}
                      className="w-full bg-[var(--bg-secondary)] border border-[var(--border-color)] rounded px-3 py-2 text-[var(--text-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-peach)] disabled:opacity-50"
                    />
                  </div>
                  <div>
                    {renderSaveButton(
                      mp.ticker,
                      () => handleSavePrice(mp.ticker, mp.date),
                      !priceInputs[mp.ticker]?.price || isNaN(parseFloat(priceInputs[mp.ticker]?.price))
                    )}
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
            onClick={handleDone}
            className="btn-accent px-4 py-2"
          >
            {allSaved ? '完成並重新計算' : '關閉'}
          </button>
        </div>
      </div>
    </div>
  );
}
