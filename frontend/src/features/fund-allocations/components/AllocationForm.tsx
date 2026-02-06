import { useEffect, useState } from 'react';

export type AllocationPurpose =
  | 'EmergencyFund'
  | 'FamilyDeposit'
  | 'General'
  | 'Savings'
  | 'Investment'
  | 'Other';

export interface AllocationFormData {
  purpose: AllocationPurpose;
  amount: number;
  note?: string;
}

interface AllocationFormProps {
  onSubmit: (data: AllocationFormData) => Promise<void> | void;
  initialData?: AllocationFormData;
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

export function AllocationForm({ onSubmit, initialData }: AllocationFormProps) {
  const [purpose, setPurpose] = useState<AllocationPurpose>(initialData?.purpose ?? DEFAULT_PURPOSE);
  const [amount, setAmount] = useState<string>(initialData?.amount != null ? String(initialData.amount) : '');
  const [note, setNote] = useState(initialData?.note ?? '');
  const [errorMessage, setErrorMessage] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    setPurpose(initialData?.purpose ?? DEFAULT_PURPOSE);
    setAmount(initialData?.amount != null ? String(initialData.amount) : '');
    setNote(initialData?.note ?? '');
    setErrorMessage('');
  }, [initialData]);

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setErrorMessage('');

    const parsedAmount = Number(amount);

    if (!amount.trim() || Number.isNaN(parsedAmount)) {
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
        note: note.trim() || undefined,
      });

      if (!initialData) {
        setPurpose(DEFAULT_PURPOSE);
        setAmount('');
        setNote('');
      }
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : '儲存資金配置失敗');
    } finally {
      setIsSubmitting(false);
    }
  };

  const isEditMode = Boolean(initialData);

  return (
    <div className="card-dark p-6 space-y-4">
      <div>
        <h3 className="text-lg font-semibold text-[var(--text-primary)]">
          {isEditMode ? '編輯資金配置' : '新增資金配置'}
        </h3>
        <p className="text-sm text-[var(--text-muted)] mt-1">設定銀行資產的用途與金額（TWD）</p>
      </div>

      <form onSubmit={handleSubmit} className="space-y-4">
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
            onChange={(event) => setPurpose(event.target.value as AllocationPurpose)}
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
            備註（選填）
          </label>
          <textarea
            value={note}
            onChange={(event) => setNote(event.target.value)}
            className="input-dark w-full h-24 resize-none"
            placeholder="例如：預留 6 個月生活費"
          />
        </div>

        <button
          type="submit"
          disabled={isSubmitting}
          className="btn-accent w-full py-2.5 disabled:opacity-50"
        >
          {isSubmitting ? '儲存中...' : isEditMode ? '更新配置' : '新增配置'}
        </button>
      </form>
    </div>
  );
}
