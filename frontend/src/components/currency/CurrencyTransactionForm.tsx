import { useState, useEffect } from 'react';
import type { CurrencyTransactionType, CreateCurrencyTransactionRequest, CurrencyTransaction } from '../../types';
import { CurrencyTransactionType as CurrencyTxType } from '../../types';

interface CurrencyTransactionFormProps {
  ledgerId: string;
  initialData?: CurrencyTransaction;
  onSubmit: (data: CreateCurrencyTransactionRequest) => Promise<void>;
  onCancel: () => void;
}

export function CurrencyTransactionForm({
  ledgerId,
  initialData,
  onSubmit,
  onCancel,
}: CurrencyTransactionFormProps) {
  const [transactionDate, setTransactionDate] = useState(
    initialData?.transactionDate?.split('T')[0] ?? new Date().toISOString().split('T')[0]
  );
  const [transactionType, setTransactionType] = useState<CurrencyTransactionType>(
    initialData?.transactionType ?? CurrencyTxType.ExchangeBuy
  );
  const [foreignAmount, setForeignAmount] = useState(
    initialData?.foreignAmount?.toString() ?? ''
  );
  const [homeAmount, setHomeAmount] = useState(
    initialData?.homeAmount?.toString() ?? ''
  );
  const [exchangeRate, setExchangeRate] = useState(
    initialData?.exchangeRate?.toString() ?? ''
  );
  const [notes, setNotes] = useState(initialData?.notes ?? '');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Exchange types need both home amount and exchange rate
  const isExchangeType =
    transactionType === CurrencyTxType.ExchangeBuy ||
    transactionType === CurrencyTxType.ExchangeSell;

  // Initial balance needs home amount (cost basis) but not exchange rate
  const needsHomeCost =
    isExchangeType || transactionType === CurrencyTxType.InitialBalance;

  // Auto-calculate exchange rate when foreign and home amounts change (only for exchange types)
  useEffect(() => {
    if (isExchangeType && foreignAmount && homeAmount) {
      const foreign = parseFloat(foreignAmount);
      const home = parseFloat(homeAmount);
      if (foreign > 0 && home > 0) {
        const rate = home / foreign;
        setExchangeRate(rate.toFixed(4));
      }
    } else if (!isExchangeType) {
      setExchangeRate('');
    }
  }, [foreignAmount, homeAmount, isExchangeType]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setIsSubmitting(true);

    try {
      await onSubmit({
        currencyLedgerId: ledgerId,
        transactionDate,
        transactionType,
        foreignAmount: parseFloat(foreignAmount),
        homeAmount: homeAmount ? parseFloat(homeAmount) : undefined,
        exchangeRate: exchangeRate ? parseFloat(exchangeRate) : undefined,
        notes: notes || undefined,
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create transaction');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      {error && (
        <div className="bg-[var(--color-danger-soft)] border border-[var(--color-danger)] text-[var(--color-danger)] p-3 rounded-lg text-base">{error}</div>
      )}

      <div>
        <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
          日期
        </label>
        <input
          type="date"
          value={transactionDate}
          onChange={(e) => setTransactionDate(e.target.value)}
          className="input-dark w-full"
          required
        />
      </div>

      <div>
        <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
          交易類型
        </label>
        <select
          value={transactionType}
          onChange={(e) => setTransactionType(Number(e.target.value) as CurrencyTransactionType)}
          className="input-dark w-full"
        >
          <option value={CurrencyTxType.ExchangeBuy}>換匯買入</option>
          <option value={CurrencyTxType.ExchangeSell}>換匯賣出</option>
          <option value={CurrencyTxType.Interest}>利息收入</option>
          <option value={CurrencyTxType.Spend}>消費支出</option>
          <option value={CurrencyTxType.InitialBalance}>期初餘額</option>
          <option value={CurrencyTxType.OtherIncome}>其他收入</option>
          <option value={CurrencyTxType.OtherExpense}>其他支出</option>
        </select>
      </div>

      <div>
        <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
          外幣金額
        </label>
        <input
          type="number"
          step="0.0001"
          value={foreignAmount}
          onChange={(e) => setForeignAmount(e.target.value)}
          className="input-dark w-full"
          placeholder="0.00"
          required
        />
      </div>

      {needsHomeCost && (
        <div>
          <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
            {transactionType === CurrencyTxType.InitialBalance ? '台幣成本' : '台幣金額'}
          </label>
          <input
            type="number"
            step="0.01"
            value={homeAmount}
            onChange={(e) => setHomeAmount(e.target.value)}
            className="input-dark w-full"
            placeholder="0.00"
            required={needsHomeCost}
          />
        </div>
      )}

      {isExchangeType && (
        <div>
          <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
            匯率（自動計算）
          </label>
          <input
            type="number"
            step="0.0001"
            value={exchangeRate}
            readOnly
            className="input-dark w-full bg-[var(--bg-tertiary)] text-[var(--text-muted)]"
            placeholder="自動計算"
          />
        </div>
      )}

      <div>
        <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
          備註
        </label>
        <textarea
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          className="input-dark w-full"
          rows={2}
          placeholder="選填"
        />
      </div>

      <div className="flex gap-3">
        <button
          type="button"
          onClick={onCancel}
          className="btn-dark flex-1 py-2"
        >
          取消
        </button>
        <button
          type="submit"
          disabled={isSubmitting}
          className="btn-accent flex-1 py-2 disabled:opacity-50"
        >
          {isSubmitting ? '處理中...' : (initialData ? '儲存' : '新增')}
        </button>
      </div>
    </form>
  );
}
