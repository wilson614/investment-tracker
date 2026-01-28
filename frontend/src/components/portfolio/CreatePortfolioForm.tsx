import { useState } from 'react';
import { X, DollarSign } from 'lucide-react';
import type { CreatePortfolioRequest } from '../../types';
import { portfolioApi } from '../../services/api';

interface CreatePortfolioFormProps {
  onClose: () => void;
  onSuccess: (portfolioId: string) => void;
}

const COMMON_CURRENCIES = ['USD', 'EUR', 'GBP', 'JPY', 'TWD'];

export function CreatePortfolioForm({ onClose, onSuccess }: CreatePortfolioFormProps) {
  const [displayName, setDisplayName] = useState('');
  const [currencyCode, setCurrencyCode] = useState('USD');
  const [homeCurrency, setHomeCurrency] = useState('TWD');
  const [initialBalance, setInitialBalance] = useState('');
  const [description, setDescription] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setIsSubmitting(true);

    try {
      const request: CreatePortfolioRequest = {
        displayName: displayName.trim() || undefined,
        currencyCode,
        homeCurrency,
        description: description.trim() || undefined,
        initialBalance: initialBalance ? parseFloat(initialBalance) : undefined,
      };

      const portfolio = await portfolioApi.create(request);
      onSuccess(portfolio.id);
    } catch (err) {
      setError(err instanceof Error ? err.message : '創建投資組合失敗');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="bg-[var(--bg-secondary)] rounded-xl shadow-2xl w-full max-w-md mx-4 overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-[var(--border-primary)]">
          <h2 className="text-lg font-semibold text-[var(--text-primary)]">
            新增投資組合
          </h2>
          <button
            onClick={onClose}
            className="p-1 hover:bg-[var(--bg-tertiary)] rounded-lg transition-colors"
          >
            <X className="w-5 h-5 text-[var(--text-secondary)]" />
          </button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} className="p-6 space-y-5">
          {/* Portfolio Type Indicator */}
          <div className="flex items-center gap-3 p-4 rounded-lg border-2 border-[var(--accent-teal)] bg-[var(--accent-teal)]/10">
            <DollarSign className="w-5 h-5 text-[var(--accent-teal)]" />
            <div className="text-left">
              <div className="text-sm font-medium text-[var(--text-primary)]">
                主要投資組合
              </div>
              <div className="text-xs text-[var(--text-muted)]">
                含匯率轉換
              </div>
            </div>
          </div>

          {/* Display Name */}
          <div className="space-y-2">
            <label className="block text-sm font-medium text-[var(--text-secondary)]">
              名稱
            </label>
            <input
              type="text"
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              placeholder="例如：美股投資組合"
              className="w-full px-4 py-2.5 bg-[var(--bg-primary)] border border-[var(--border-primary)] rounded-lg text-[var(--text-primary)] placeholder:text-[var(--text-muted)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-teal)]/50 focus:border-[var(--accent-teal)]"
              maxLength={100}
            />
          </div>

          {/* Currency Selection */}
          <div className="grid gap-4 grid-cols-2">
            <div className="space-y-2">
              <label className="block text-sm font-medium text-[var(--text-secondary)]">
                投資組合幣別
              </label>
              <select
                value={currencyCode}
                onChange={(e) => setCurrencyCode(e.target.value)}
                className="w-full px-4 py-2.5 bg-[var(--bg-primary)] border border-[var(--border-primary)] rounded-lg text-[var(--text-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-teal)]/50 focus:border-[var(--accent-teal)]"
              >
                {COMMON_CURRENCIES.map((currency) => (
                  <option key={currency} value={currency}>
                    {currency}
                  </option>
                ))}
              </select>
              <p className="text-xs text-[var(--text-muted)]">
                系統將自動建立對應幣別的帳本
              </p>
            </div>

            <div className="space-y-2">
              <label className="block text-sm font-medium text-[var(--text-secondary)]">
                本國幣別
              </label>
              <select
                value={homeCurrency}
                onChange={(e) => setHomeCurrency(e.target.value)}
                className="w-full px-4 py-2.5 bg-[var(--bg-primary)] border border-[var(--border-primary)] rounded-lg text-[var(--text-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-teal)]/50 focus:border-[var(--accent-teal)]"
              >
                {COMMON_CURRENCIES.map((currency) => (
                  <option key={currency} value={currency}>
                    {currency}
                  </option>
                ))}
              </select>
            </div>
          </div>

          {/* Initial Balance (optional) */}
          <div className="space-y-2">
            <label className="block text-sm font-medium text-[var(--text-secondary)]">
              初始餘額 <span className="text-[var(--text-muted)]">(選填)</span>
            </label>
            <input
              type="number"
              value={initialBalance}
              onChange={(e) => setInitialBalance(e.target.value)}
              placeholder="0"
              min="0"
              step="0.01"
              className="w-full px-4 py-2.5 bg-[var(--bg-primary)] border border-[var(--border-primary)] rounded-lg text-[var(--text-primary)] placeholder:text-[var(--text-muted)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-teal)]/50 focus:border-[var(--accent-teal)]"
            />
          </div>

          {/* Description */}
          <div className="space-y-2">
            <label className="block text-sm font-medium text-[var(--text-secondary)]">
              備註 <span className="text-[var(--text-muted)]">(選填)</span>
            </label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="投資組合說明..."
              rows={2}
              className="w-full px-4 py-2.5 bg-[var(--bg-primary)] border border-[var(--border-primary)] rounded-lg text-[var(--text-primary)] placeholder:text-[var(--text-muted)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-teal)]/50 focus:border-[var(--accent-teal)] resize-none"
              maxLength={500}
            />
          </div>

          {/* Error Message */}
          {error && (
            <div className="p-3 bg-[var(--color-error)]/10 border border-[var(--color-error)]/30 rounded-lg text-sm text-[var(--color-error)]">
              {error}
            </div>
          )}

          {/* Buttons */}
          <div className="flex gap-3 pt-2">
            <button
              type="button"
              onClick={onClose}
              disabled={isSubmitting}
              className="flex-1 px-4 py-2.5 bg-[var(--bg-tertiary)] hover:bg-[var(--border-primary)] text-[var(--text-secondary)] rounded-lg font-medium transition-colors disabled:opacity-50"
            >
              取消
            </button>
            <button
              type="submit"
              disabled={isSubmitting}
              className="flex-1 px-4 py-2.5 bg-[var(--accent-teal)] hover:bg-[var(--accent-teal)]/90 text-white rounded-lg font-medium transition-colors disabled:opacity-50"
            >
              {isSubmitting ? '創建中...' : '創建'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
