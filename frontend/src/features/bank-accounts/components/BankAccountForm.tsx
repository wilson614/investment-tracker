import { useState, useEffect } from 'react';
import { X, Save, Building2 } from 'lucide-react';
import type { BankAccount, CreateBankAccountRequest, UpdateBankAccountRequest } from '../types';

const CURRENCY_OPTIONS = [
  { value: 'TWD', label: '新台幣' },
  { value: 'USD', label: '美元' },
  { value: 'EUR', label: '歐元' },
  { value: 'JPY', label: '日圓' },
  { value: 'CNY', label: '人民幣' },
  { value: 'GBP', label: '英鎊' },
  { value: 'AUD', label: '澳幣' },
] as const;

const DEFAULT_CURRENCY = 'TWD';
type SupportedCurrency = (typeof CURRENCY_OPTIONS)[number]['value'];

const isSupportedCurrency = (currency: string): currency is SupportedCurrency =>
  CURRENCY_OPTIONS.some((option) => option.value === currency);

interface BankAccountFormProps {
  initialData?: BankAccount;
  onSubmit: (data: CreateBankAccountRequest | UpdateBankAccountRequest) => Promise<boolean>;
  onCancel: () => void;
  isLoading: boolean;
}

export function BankAccountForm({ initialData, onSubmit, onCancel, isLoading }: BankAccountFormProps) {
  const [bankName, setBankName] = useState('');
  const [totalAssets, setTotalAssets] = useState<string>('');
  const [currency, setCurrency] = useState<SupportedCurrency>(DEFAULT_CURRENCY);
  const [interestRate, setInterestRate] = useState<string>('');
  const [interestCap, setInterestCap] = useState<string>('');
  const [note, setNote] = useState('');
  const [errorMessage, setErrorMessage] = useState<string>('');

  useEffect(() => {
    if (initialData) {
      setBankName(initialData.bankName);
      setTotalAssets(initialData.totalAssets.toString());
      setCurrency(isSupportedCurrency(initialData.currency) ? initialData.currency : DEFAULT_CURRENCY);
      setInterestRate(initialData.interestRate.toString());
      setInterestCap(initialData.interestCap !== undefined ? initialData.interestCap.toString() : '');
      setNote(initialData.note || '');
    } else {
      setCurrency(DEFAULT_CURRENCY);
    }
    setErrorMessage('');
  }, [initialData]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErrorMessage('');

    const assets = parseFloat(totalAssets);
    const rate = parseFloat(interestRate);
    const cap = interestCap === '' ? undefined : parseFloat(interestCap);

    if (!bankName || isNaN(assets) || isNaN(rate) || (cap !== undefined && isNaN(cap))) {
      return;
    }

    if (assets < 0) {
      setErrorMessage('總資產不能為負數');
      return;
    }

    if (rate < 0) {
      setErrorMessage('年利率不能為負數');
      return;
    }

    if (cap !== undefined && cap < 0) {
      setErrorMessage('優惠上限不能為負數');
      return;
    }

    const data: CreateBankAccountRequest = {
      bankName,
      totalAssets: assets,
      interestRate: rate,
      interestCap: cap,
      note: note.trim() || undefined,
      currency
    };

    await onSubmit(data);
  };

  return (
    <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50 p-4">
      <div className="card-dark w-full max-w-lg overflow-hidden flex flex-col max-h-[90vh]">
        <div className="p-6 border-b border-[var(--border-color)] flex justify-between items-center">
          <h2 className="text-xl font-bold text-[var(--text-primary)] flex items-center gap-2">
            <Building2 className="w-5 h-5 text-[var(--accent-peach)]" />
            {initialData ? '編輯銀行帳戶' : '新增銀行帳戶'}
          </h2>
          <button
            onClick={onCancel}
            className="text-[var(--text-secondary)] hover:text-[var(--text-primary)] transition-colors"
          >
            <X className="w-6 h-6" />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="p-6 overflow-y-auto space-y-4">
          {errorMessage ? (
            <div
              className="p-3 rounded border border-red-500/40 bg-red-500/10 text-red-200 text-sm"
              role="alert"
            >
              {errorMessage}
            </div>
          ) : null}
          <div>
            <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
              銀行名稱
            </label>
            <input
              type="text"
              value={bankName}
              onChange={(e) => setBankName(e.target.value)}
              className="input-dark w-full"
              required
            />
          </div>

          <div>
            <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
              總資產 ({currency})
            </label>
            <input
              type="number"
              value={totalAssets}
              onChange={(e) => setTotalAssets(e.target.value)}
              className="input-dark w-full"
              required
              min="0"
              step="1"
            />
          </div>

          <div>
            <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
              幣別
            </label>
            <select
              value={currency}
              onChange={(e) => {
                const selectedCurrency = e.target.value;
                if (isSupportedCurrency(selectedCurrency)) {
                  setCurrency(selectedCurrency);
                }
              }}
              className="input-dark w-full"
              required
            >
              {CURRENCY_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.value} - {option.label}
                </option>
              ))}
            </select>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                年利率 (%)
              </label>
              <input
                type="number"
                value={interestRate}
                onChange={(e) => setInterestRate(e.target.value)}
                className="input-dark w-full"
                required
                min="0"
                step="0.01"
              />
            </div>
            <div>
              <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                優惠上限 ({currency})
              </label>
              <input
                type="number"
                value={interestCap}
                onChange={(e) => setInterestCap(e.target.value)}
                className="input-dark w-full"
                min="0"
                step="1"
                placeholder="留空為無上限"
              />
            </div>
          </div>

          <div>
            <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
              備註
            </label>
            <textarea
              value={note}
              onChange={(e) => setNote(e.target.value)}
              className="input-dark w-full h-24 resize-none"
            />
          </div>

          <div className="flex gap-3 pt-4">
            <button
              type="button"
              onClick={onCancel}
              className="btn-dark flex-1 py-2.5"
            >
              取消
            </button>
            <button
              type="submit"
              disabled={isLoading}
              className="btn-accent flex items-center justify-center gap-2 flex-1 py-2.5 disabled:opacity-50"
            >
              <Save className="w-4 h-4" />
              {isLoading ? '儲存中...' : '儲存'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
