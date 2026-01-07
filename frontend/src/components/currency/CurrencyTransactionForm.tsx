import { useState, useEffect } from 'react';
import type { CurrencyTransactionType, CreateCurrencyTransactionRequest } from '../../types';
import { CurrencyTransactionType as CurrencyTxType } from '../../types';

interface CurrencyTransactionFormProps {
  ledgerId: string;
  onSubmit: (data: CreateCurrencyTransactionRequest) => Promise<void>;
  onCancel: () => void;
}

export function CurrencyTransactionForm({
  ledgerId,
  onSubmit,
  onCancel,
}: CurrencyTransactionFormProps) {
  const [transactionDate, setTransactionDate] = useState(
    new Date().toISOString().split('T')[0]
  );
  const [transactionType, setTransactionType] = useState<CurrencyTransactionType>(
    CurrencyTxType.ExchangeBuy
  );
  const [foreignAmount, setForeignAmount] = useState('');
  const [homeAmount, setHomeAmount] = useState('');
  const [exchangeRate, setExchangeRate] = useState('');
  const [notes, setNotes] = useState('');
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
        <div className="bg-red-50 text-red-700 p-3 rounded-lg">{error}</div>
      )}

      <div>
        <label className="block text-sm font-medium text-gray-700 mb-1">
          日期
        </label>
        <input
          type="date"
          value={transactionDate}
          onChange={(e) => setTransactionDate(e.target.value)}
          className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          required
        />
      </div>

      <div>
        <label className="block text-sm font-medium text-gray-700 mb-1">
          交易類型
        </label>
        <select
          value={transactionType}
          onChange={(e) => setTransactionType(Number(e.target.value) as CurrencyTransactionType)}
          className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent"
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
        <label className="block text-sm font-medium text-gray-700 mb-1">
          外幣金額
        </label>
        <input
          type="number"
          step="0.0001"
          value={foreignAmount}
          onChange={(e) => setForeignAmount(e.target.value)}
          className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          placeholder="0.00"
          required
        />
      </div>

      {needsHomeCost && (
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">
            {transactionType === CurrencyTxType.InitialBalance ? '台幣成本' : '台幣金額'}
          </label>
          <input
            type="number"
            step="0.01"
            value={homeAmount}
            onChange={(e) => setHomeAmount(e.target.value)}
            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            placeholder="0.00"
            required={needsHomeCost}
          />
        </div>
      )}

      {isExchangeType && (
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">
            匯率（自動計算）
          </label>
          <input
            type="number"
            step="0.0001"
            value={exchangeRate}
            readOnly
            className="w-full px-3 py-2 border border-gray-300 rounded-lg bg-gray-50 text-gray-600"
            placeholder="自動計算"
          />
        </div>
      )}

      <div>
        <label className="block text-sm font-medium text-gray-700 mb-1">
          備註
        </label>
        <textarea
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          rows={2}
          placeholder="選填"
        />
      </div>

      <div className="flex gap-3">
        <button
          type="button"
          onClick={onCancel}
          className="flex-1 px-4 py-2 text-gray-700 bg-gray-100 rounded-lg hover:bg-gray-200 transition-colors"
        >
          取消
        </button>
        <button
          type="submit"
          disabled={isSubmitting}
          className="flex-1 px-4 py-2 text-white bg-blue-600 rounded-lg hover:bg-blue-700 disabled:opacity-50 transition-colors"
        >
          {isSubmitting ? '處理中...' : '新增'}
        </button>
      </div>
    </form>
  );
}
