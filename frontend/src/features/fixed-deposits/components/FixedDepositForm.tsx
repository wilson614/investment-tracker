import { useEffect, useMemo, useState } from 'react';
import { Save, X, Landmark } from 'lucide-react';
import type { BankAccount } from '../../bank-accounts/types';
import type {
  CreateFixedDepositRequest,
  FixedDepositResponse,
} from '../types';

interface FixedDepositFormProps {
  bankAccounts: BankAccount[];
  initialData?: FixedDepositResponse;
  onSubmit: (data: CreateFixedDepositRequest) => Promise<boolean>;
  onCancel: () => void;
  isLoading: boolean;
}

export function FixedDepositForm({
  bankAccounts,
  initialData,
  onSubmit,
  onCancel,
  isLoading,
}: FixedDepositFormProps) {
  const selectableBankAccounts = useMemo(
    () => bankAccounts.filter((account) => account.isActive),
    [bankAccounts]
  );

  const defaultBankAccountId = selectableBankAccounts[0]?.id ?? '';

  const [bankAccountId, setBankAccountId] = useState('');
  const [principal, setPrincipal] = useState('');
  const [annualInterestRate, setAnnualInterestRate] = useState('');
  const [termMonths, setTermMonths] = useState('12');
  const [startDate, setStartDate] = useState('');
  const [note, setNote] = useState('');
  const [errorMessage, setErrorMessage] = useState('');

  useEffect(() => {
    if (initialData) {
      setBankAccountId(initialData.bankAccountId);
      setPrincipal(initialData.principal.toString());
      setAnnualInterestRate(initialData.annualInterestRate.toString());
      setTermMonths(initialData.termMonths.toString());
      setStartDate(initialData.startDate.slice(0, 10));
      setNote(initialData.note ?? '');
    } else {
      setBankAccountId(defaultBankAccountId);
      setPrincipal('');
      setAnnualInterestRate('');
      setTermMonths('12');
      setStartDate(new Date().toISOString().slice(0, 10));
      setNote('');
    }
    setErrorMessage('');
  }, [defaultBankAccountId, initialData]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErrorMessage('');

    const parsedPrincipal = Number(principal);
    const parsedRate = Number(annualInterestRate);
    const parsedTermMonths = Number(termMonths);

    if (!bankAccountId) {
      setErrorMessage('請選擇銀行帳戶');
      return;
    }

    if (!Number.isFinite(parsedPrincipal) || parsedPrincipal <= 0) {
      setErrorMessage('本金需大於 0');
      return;
    }

    if (!Number.isFinite(parsedRate) || parsedRate < 0) {
      setErrorMessage('年利率不能為負數');
      return;
    }

    if (!Number.isInteger(parsedTermMonths) || parsedTermMonths <= 0) {
      setErrorMessage('期數（月）需為大於 0 的整數');
      return;
    }

    if (!startDate) {
      setErrorMessage('請選擇起始日');
      return;
    }

    const payload: CreateFixedDepositRequest = {
      bankAccountId,
      principal: parsedPrincipal,
      annualInterestRate: parsedRate,
      termMonths: parsedTermMonths,
      startDate,
      note: note.trim() || undefined,
    };

    await onSubmit(payload);
  };

  return (
    <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50 p-4">
      <div className="card-dark w-full max-w-lg overflow-hidden flex flex-col max-h-[90vh]">
        <div className="p-6 border-b border-[var(--border-color)] flex justify-between items-center">
          <h2 className="text-xl font-bold text-[var(--text-primary)] flex items-center gap-2">
            <Landmark className="w-5 h-5 text-[var(--accent-peach)]" />
            {initialData ? '編輯定存' : '新增定存'}
          </h2>
          <button
            type="button"
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
              銀行帳戶
            </label>
            <select
              value={bankAccountId}
              onChange={(e) => setBankAccountId(e.target.value)}
              className="input-dark w-full"
              required
              disabled={selectableBankAccounts.length === 0 || Boolean(initialData)}
            >
              {selectableBankAccounts.length === 0 ? (
                <option value="">無可用銀行帳戶</option>
              ) : null}
              {selectableBankAccounts.map((account) => (
                <option key={account.id} value={account.id}>
                  {account.bankName} ({account.currency})
                </option>
              ))}
            </select>
            {initialData ? (
              <p className="text-xs text-[var(--text-muted)] mt-1">編輯模式不可變更關聯帳戶</p>
            ) : null}
          </div>

          <div>
            <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
              本金
            </label>
            <input
              type="number"
              value={principal}
              onChange={(e) => setPrincipal(e.target.value)}
              className="input-dark w-full"
              min="0"
              step="1"
              required
            />
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div>
              <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                年利率 (%)
              </label>
              <input
                type="number"
                value={annualInterestRate}
                onChange={(e) => setAnnualInterestRate(e.target.value)}
                className="input-dark w-full"
                min="0"
                step="0.01"
                required
              />
            </div>
            <div>
              <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                期數（月）
              </label>
              <input
                type="number"
                value={termMonths}
                onChange={(e) => setTermMonths(e.target.value)}
                className="input-dark w-full"
                min="1"
                step="1"
                required
              />
            </div>
          </div>

          <div>
            <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
              起始日
            </label>
            <input
              type="date"
              value={startDate}
              onChange={(e) => setStartDate(e.target.value)}
              className="input-dark w-full"
              required
            />
          </div>

          <div>
            <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
              備註
            </label>
            <textarea
              value={note}
              onChange={(e) => setNote(e.target.value)}
              className="input-dark w-full h-24 resize-none"
              maxLength={500}
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
              disabled={isLoading || selectableBankAccounts.length === 0}
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
