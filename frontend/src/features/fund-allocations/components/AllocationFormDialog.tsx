import { useEffect, useState, type FormEvent } from 'react';
import { X } from 'lucide-react';
import type { AllocationPurpose } from '../types';

interface AllocationFormDialogSubmitData {
  purpose: AllocationPurpose;
  amount: number;
  isDisposable: boolean;
  note?: string;
}

interface AllocationFormDialogInitialData {
  purpose: AllocationPurpose;
  amount: number;
  isDisposable?: boolean;
  note?: string;
}

interface AllocationFormDialogProps {
  isOpen: boolean;
  onClose: () => void;
  onSubmit: (data: AllocationFormDialogSubmitData) => Promise<void> | void;
  initialData?: AllocationFormDialogInitialData;
}

const PURPOSE_OPTIONS: ReadonlyArray<{ value: AllocationPurpose; label: string }> = [
  { value: 'EmergencyFund', label: '緊急預備金' },
  { value: 'FamilyDeposit', label: '家庭存款' },
  { value: 'General', label: '一般用途' },
  { value: 'Savings', label: '儲蓄' },
  { value: 'Investment', label: '投資準備金' },
  { value: 'Other', label: '其他' },
];

const DEFAULT_PURPOSE: AllocationPurpose = 'EmergencyFund';

function resolvePurpose(value?: AllocationPurpose): AllocationPurpose {
  return value ?? DEFAULT_PURPOSE;
}

function getDefaultIsDisposable(purpose: AllocationPurpose): boolean {
  return purpose !== 'EmergencyFund' && purpose !== 'FamilyDeposit';
}

function resolveIsDisposable(value: boolean | undefined, purpose: AllocationPurpose): boolean {
  return value ?? getDefaultIsDisposable(purpose);
}

export function AllocationFormDialog({ isOpen, onClose, onSubmit, initialData }: AllocationFormDialogProps) {
  const [purpose, setPurpose] = useState<AllocationPurpose>(DEFAULT_PURPOSE);
  const [amount, setAmount] = useState<string>('');
  const [isDisposable, setIsDisposable] = useState<boolean>(getDefaultIsDisposable(DEFAULT_PURPOSE));
  const [note, setNote] = useState('');
  const [errorMessage, setErrorMessage] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    if (!isOpen) return;

    const resolvedPurpose = resolvePurpose(initialData?.purpose);

    setPurpose(resolvedPurpose);
    setAmount(initialData?.amount != null ? String(initialData.amount) : '');
    setIsDisposable(resolveIsDisposable(initialData?.isDisposable, resolvedPurpose));
    setNote(initialData?.note ?? '');
    setErrorMessage('');
  }, [isOpen, initialData]);

  if (!isOpen) return null;

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setErrorMessage('');

    if (!amount.trim()) {
      setErrorMessage('請輸入金額');
      return;
    }

    const parsedAmount = Number(amount);

    if (Number.isNaN(parsedAmount)) {
      setErrorMessage('請輸入有效的金額');
      return;
    }

    if (parsedAmount < 0) {
      setErrorMessage('金額不能為負數');
      return;
    }

    setIsSubmitting(true);

    try {
      await onSubmit({
        purpose,
        amount: parsedAmount,
        isDisposable,
        note: note.trim() || undefined,
      });
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : '儲存資金配置失敗');
    } finally {
      setIsSubmitting(false);
    }
  };

  const isEditMode = Boolean(initialData);

  return (
    <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50 p-4">
      <div className="card-dark p-0 w-full max-w-lg overflow-hidden flex flex-col max-h-[90vh]">
        <div className="flex items-center justify-between px-6 py-4 border-b border-[var(--border-color)]">
          <h2 className="text-lg font-bold text-[var(--text-primary)]">
            {isEditMode ? '編輯資金配置' : '新增資金配置'}
          </h2>
          <button
            type="button"
            onClick={onClose}
            disabled={isSubmitting}
            className="p-1 text-[var(--text-muted)] hover:text-[var(--text-primary)] rounded transition-colors disabled:opacity-50"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="flex flex-col min-h-0">
          <div className="p-6 overflow-y-auto space-y-4">
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
                用途
              </label>
              <select
                value={purpose}
                onChange={(event) => {
                  const nextPurpose = event.target.value as AllocationPurpose;
                  setPurpose(nextPurpose);
                  setIsDisposable(getDefaultIsDisposable(nextPurpose));
                }}
                className="input-dark w-full"
                required
              >
                {PURPOSE_OPTIONS.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </div>

            <div className="rounded-lg border border-[var(--border-color)] bg-[var(--bg-secondary)]/30 p-3">
              <div className="flex items-center justify-between gap-3">
                <div>
                  <p className="text-sm font-medium text-[var(--text-secondary)]">可動用資金</p>
                  <p className="text-xs text-[var(--text-muted)] mt-1">勾選後，此配置會被視為可動用存款</p>
                </div>
                <label className="inline-flex items-center gap-2 text-sm text-[var(--text-secondary)] select-none">
                  <input
                    type="checkbox"
                    checked={isDisposable}
                    onChange={(event) => setIsDisposable(event.target.checked)}
                    className="checkbox-dark"
                    disabled={isSubmitting}
                  />
                  <span>{isDisposable ? '可動用' : '不可動用'}</span>
                </label>
              </div>
            </div>

            <div>
              <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                金額 (TWD)
              </label>
              <input
                type="number"
                value={amount}
                onChange={(event) => setAmount(event.target.value)}
                className="input-dark w-full"
                min="0"
                step="1"
                required
              />
            </div>

            <div>
              <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                備註
              </label>
              <textarea
                value={note}
                onChange={(event) => setNote(event.target.value)}
                className="input-dark w-full h-24 resize-none"
              />
            </div>
          </div>

          <div className="flex justify-end gap-3 px-6 py-4 border-t border-[var(--border-color)] bg-[var(--bg-secondary)]/30">
            <button
              type="button"
              onClick={onClose}
              disabled={isSubmitting}
              className="btn-dark px-4 py-2 disabled:opacity-50"
            >
              取消
            </button>
            <button
              type="submit"
              disabled={isSubmitting}
              className="btn-accent px-4 py-2 disabled:opacity-50"
            >
              {isSubmitting ? '儲存中...' : isEditMode ? '更新配置' : '儲存配置'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
