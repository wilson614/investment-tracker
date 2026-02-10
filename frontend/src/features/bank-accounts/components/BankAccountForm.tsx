import { useState, useEffect } from 'react';
import { X, Save, Building2 } from 'lucide-react';
import type {
  BankAccount,
  BankAccountType,
  CreateBankAccountRequest,
  UpdateBankAccountRequest
} from '../types';

const CURRENCY_OPTIONS = [
  { value: 'TWD', label: '新台幣' },
  { value: 'USD', label: '美元' },
  { value: 'EUR', label: '歐元' },
  { value: 'JPY', label: '日圓' },
  { value: 'CNY', label: '人民幣' },
  { value: 'GBP', label: '英鎊' },
  { value: 'AUD', label: '澳幣' },
] as const;

const ACCOUNT_TYPE_OPTIONS: Array<{ value: BankAccountType; label: string }> = [
  { value: 'Savings', label: '活存帳戶' },
  { value: 'FixedDeposit', label: '定存帳戶' },
];

const DEFAULT_CURRENCY = 'TWD';
const DEFAULT_TERM_MONTHS = '12';

type SupportedCurrency = (typeof CURRENCY_OPTIONS)[number]['value'];

const isSupportedCurrency = (currency: string): currency is SupportedCurrency =>
  CURRENCY_OPTIONS.some((option) => option.value === currency);

const getTodayDate = () => new Date().toISOString().slice(0, 10);

interface BankAccountFormProps {
  initialData?: BankAccount;
  onSubmit: (data: CreateBankAccountRequest | UpdateBankAccountRequest) => Promise<boolean>;
  onCancel: () => void;
  isLoading: boolean;
}

export function BankAccountForm({ initialData, onSubmit, onCancel, isLoading }: BankAccountFormProps) {
  const [bankName, setBankName] = useState(initialData?.bankName ?? '');
  const [accountType, setAccountType] = useState<BankAccountType>(initialData?.accountType ?? 'Savings');
  const [totalAssets, setTotalAssets] = useState<string>(initialData?.totalAssets != null ? initialData.totalAssets.toString() : '');
  const [currency, setCurrency] = useState<SupportedCurrency>(initialData?.currency && isSupportedCurrency(initialData.currency) ? initialData.currency : DEFAULT_CURRENCY);
  const [interestRate, setInterestRate] = useState<string>(initialData?.interestRate != null ? initialData.interestRate.toString() : '');
  const [interestCap, setInterestCap] = useState<string>(initialData?.interestCap != null ? initialData.interestCap.toString() : '');
  const [termMonths, setTermMonths] = useState<string>(initialData?.termMonths != null ? initialData.termMonths.toString() : DEFAULT_TERM_MONTHS);
  const [startDate, setStartDate] = useState<string>(initialData?.startDate ? initialData.startDate.slice(0, 10) : getTodayDate());
  const [note, setNote] = useState(initialData?.note || '');
  const [errorMessage, setErrorMessage] = useState<string>('');

  const isFixedDeposit = accountType === 'FixedDeposit';

  useEffect(() => {
    if (initialData) {
      setBankName(initialData.bankName);
      setAccountType(initialData.accountType ?? 'Savings');
      setTotalAssets(initialData.totalAssets.toString());
      setCurrency(isSupportedCurrency(initialData.currency) ? initialData.currency : DEFAULT_CURRENCY);
      setInterestRate(initialData.interestRate.toString());
      setInterestCap(initialData.interestCap != null ? initialData.interestCap.toString() : '');
      setTermMonths(initialData.termMonths != null ? initialData.termMonths.toString() : DEFAULT_TERM_MONTHS);
      setStartDate(initialData.startDate ? initialData.startDate.slice(0, 10) : getTodayDate());
      setNote(initialData.note || '');
    } else {
      setBankName('');
      setAccountType('Savings');
      setTotalAssets('');
      setCurrency(DEFAULT_CURRENCY);
      setInterestRate('');
      setInterestCap('');
      setTermMonths(DEFAULT_TERM_MONTHS);
      setStartDate(getTodayDate());
      setNote('');
    }
    setErrorMessage('');
  }, [initialData]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErrorMessage('');

    const trimmedBankName = bankName.trim();
    const assets = parseFloat(totalAssets);
    const rate = parseFloat(interestRate);
    const cap = interestCap === '' ? undefined : parseFloat(interestCap);
    const parsedTermMonths = termMonths === '' ? NaN : Number(termMonths);

    if (!trimmedBankName || isNaN(assets) || isNaN(rate) || (cap !== undefined && isNaN(cap))) {
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

    if (isFixedDeposit) {
      if (!Number.isInteger(parsedTermMonths) || parsedTermMonths <= 0) {
        setErrorMessage('定存期數（月）需為大於 0 的整數');
        return;
      }

      if (!startDate) {
        setErrorMessage('請選擇定存起始日');
        return;
      }
    }

    const data: CreateBankAccountRequest = {
      bankName: trimmedBankName,
      totalAssets: assets,
      interestRate: rate,
      interestCap: cap,
      note: note.trim() || undefined,
      currency,
      accountType,
      termMonths: isFixedDeposit ? parsedTermMonths : undefined,
      startDate: isFixedDeposit ? startDate : undefined,
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
              帳戶類型
            </label>
            <select
              value={accountType}
              onChange={(e) => setAccountType(e.target.value as BankAccountType)}
              className="input-dark w-full"
              required
            >
              {ACCOUNT_TYPE_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
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
              step="any"
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

          {isFixedDeposit && (
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                  定存期數（月）
                </label>
                <input
                  type="number"
                  value={termMonths}
                  onChange={(e) => setTermMonths(e.target.value)}
                  className="input-dark w-full"
                  min="1"
                  step="1"
                  required={isFixedDeposit}
                />
              </div>
              <div>
                <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                  定存起始日
                </label>
                <input
                  type="date"
                  value={startDate}
                  onChange={(e) => setStartDate(e.target.value)}
                  className="input-dark w-full"
                  required={isFixedDeposit}
                />
              </div>
            </div>
          )}

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
