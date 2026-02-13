import { useState, useEffect } from 'react';
import type { CurrencyTransactionType, CreateCurrencyTransactionRequest, CurrencyTransaction } from '../../types';
import { CurrencyTransactionType as CurrencyTxType } from '../../types';

interface CurrencyTransactionFormProps {
  ledgerId: string;
  currencyCode: string;
  initialData?: CurrencyTransaction;
  onSubmit: (data: CreateCurrencyTransactionRequest) => Promise<void>;
  onCancel: () => void;
}

const redesignedTransactionTypeNameToValue: Record<string, CurrencyTransactionType> = {
  ExchangeBuy: CurrencyTxType.ExchangeBuy,
  ExchangeSell: CurrencyTxType.ExchangeSell,
  Deposit: CurrencyTxType.Deposit,
  Withdraw: CurrencyTxType.Withdraw,
  Interest: CurrencyTxType.Interest,
  Spend: CurrencyTxType.Spend,
  InitialBalance: CurrencyTxType.InitialBalance,
  TransferInBalance: CurrencyTxType.InitialBalance,
  OtherIncome: CurrencyTxType.OtherIncome,
  OtherExpense: CurrencyTxType.OtherExpense,
};

const validTransactionTypes = new Set<number>(Object.values(CurrencyTxType));

function normalizeTransactionType(
  type: CurrencyTransaction['transactionType'] | string | undefined,
  fallback: CurrencyTransactionType
): CurrencyTransactionType {
  if (typeof type === 'number' && validTransactionTypes.has(type)) {
    return type as CurrencyTransactionType;
  }

  if (typeof type === 'string') {
    const normalized = type.trim();
    if (normalized.length > 0) {
      const byName = redesignedTransactionTypeNameToValue[normalized];
      if (byName !== undefined) {
        return byName;
      }

      const byNumber = Number(normalized);
      if (!Number.isNaN(byNumber) && validTransactionTypes.has(byNumber)) {
        return byNumber as CurrencyTransactionType;
      }
    }
  }

  return fallback;
}

export function CurrencyTransactionForm({
  ledgerId,
  currencyCode,
  initialData,
  onSubmit,
  onCancel,
}: CurrencyTransactionFormProps) {
  const isTwd = currencyCode === 'TWD';
  const defaultTransactionType = isTwd ? CurrencyTxType.Deposit : CurrencyTxType.ExchangeBuy;

  const [transactionDate, setTransactionDate] = useState(
    initialData?.transactionDate?.split('T')[0] ?? new Date().toISOString().split('T')[0]
  );
  const [transactionType, setTransactionType] = useState<CurrencyTransactionType>(
    normalizeTransactionType(
      initialData?.transactionType as CurrencyTransaction['transactionType'] | string | undefined,
      defaultTransactionType
    )
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
  // For TWD ledger, exchange types usually don't make sense or are 1:1, so we treat them differently
  const isExchangeType =
    !isTwd &&
    (transactionType === CurrencyTxType.ExchangeBuy ||
    transactionType === CurrencyTxType.ExchangeSell);

  // Initial balance needs home amount (cost basis) but not exchange rate
  // For TWD, home amount is same as foreign amount (amount itself), so we don't need separate input
  const needsHomeCost =
    !isTwd &&
    (isExchangeType || transactionType === CurrencyTxType.InitialBalance);

  useEffect(() => {
    if (!needsHomeCost) {
      setHomeAmount('');
    }
  }, [needsHomeCost]);

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

  // Ensure valid transaction type for TWD
  useEffect(() => {
    if (isTwd && (transactionType === CurrencyTxType.ExchangeBuy || transactionType === CurrencyTxType.ExchangeSell)) {
      setTransactionType(CurrencyTxType.Deposit);
    }
  }, [isTwd, transactionType]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    // Validate TWD amount is an integer
    if (needsHomeCost && homeAmount) {
      const homeValue = parseFloat(homeAmount);
      if (!Number.isInteger(homeValue)) {
        setError('台幣金額必須為整數');
        return;
      }
    }

    setIsSubmitting(true);

    try {
      await onSubmit({
        currencyLedgerId: ledgerId,
        transactionDate,
        transactionType,
        foreignAmount: parseFloat(foreignAmount),
        homeAmount: needsHomeCost && homeAmount ? parseFloat(homeAmount) : undefined,
        exchangeRate: isTwd ? 1 : (exchangeRate ? parseFloat(exchangeRate) : undefined),
        notes: notes || undefined,
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : '新增交易失敗');
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
          {isTwd ? (
             /* TWD-specific transaction types */
             <>
               <option value={CurrencyTxType.Deposit}>存入</option>
               <option value={CurrencyTxType.Withdraw}>提領</option>
               <option value={CurrencyTxType.Interest}>利息收入</option>
               <option value={CurrencyTxType.Spend}>消費支出</option>
               <option value={CurrencyTxType.InitialBalance}>轉入餘額</option>
               <option value={CurrencyTxType.OtherIncome}>其他收入</option>
               <option value={CurrencyTxType.OtherExpense}>其他支出</option>
             </>
          ) : (
            /* Foreign currency transaction types */
            <>
              <option value={CurrencyTxType.ExchangeBuy}>換匯買入</option>
              <option value={CurrencyTxType.ExchangeSell}>換匯賣出</option>
              <option value={CurrencyTxType.Deposit}>存入</option>
              <option value={CurrencyTxType.Withdraw}>提領</option>
              <option value={CurrencyTxType.Interest}>利息收入</option>
              <option value={CurrencyTxType.Spend}>消費支出</option>
              <option value={CurrencyTxType.InitialBalance}>轉入餘額</option>
              <option value={CurrencyTxType.OtherIncome}>其他收入</option>
              <option value={CurrencyTxType.OtherExpense}>其他支出</option>
            </>
          )}
        </select>
      </div>

      <div>
        <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
          {isTwd ? '金額' : '外幣金額'}
        </label>
        <input
          type="number"
          step="0.0001"
          value={foreignAmount}
          onChange={(e) => setForeignAmount(e.target.value)}
          className="input-dark w-full"
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
            step="1"
            value={homeAmount}
            onChange={(e) => setHomeAmount(e.target.value)}
            className="input-dark w-full"
            required={needsHomeCost}
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
