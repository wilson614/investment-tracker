import { useEffect, useState } from 'react';
import { X, Save, CreditCard } from 'lucide-react';
import type {
  CreditCardResponse,
  CreateCreditCardRequest,
  UpdateCreditCardRequest,
} from '../types';

interface CreditCardFormProps {
  initialData?: CreditCardResponse;
  onSubmit: (data: CreateCreditCardRequest | UpdateCreditCardRequest) => Promise<boolean>;
  onCancel: () => void;
  isLoading: boolean;
}

export function CreditCardForm({ initialData, onSubmit, onCancel, isLoading }: CreditCardFormProps) {
  const [bankName, setBankName] = useState('');
  const [cardName, setCardName] = useState('');
  const [billingCycleDay, setBillingCycleDay] = useState<string>('1');
  const [note, setNote] = useState('');
  const [errorMessage, setErrorMessage] = useState('');

  useEffect(() => {
    if (initialData) {
      setBankName(initialData.bankName);
      setCardName(initialData.cardName);
      setBillingCycleDay(initialData.billingCycleDay.toString());
      setNote(initialData.note ?? '');
    } else {
      setBankName('');
      setCardName('');
      setBillingCycleDay('1');
      setNote('');
    }
    setErrorMessage('');
  }, [initialData]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErrorMessage('');

    const normalizedBankName = bankName.trim();
    const normalizedCardName = cardName.trim();
    const parsedBillingCycleDay = Number(billingCycleDay);

    if (!normalizedBankName || !normalizedCardName) {
      setErrorMessage('銀行名稱與卡片名稱為必填');
      return;
    }

    if (!Number.isInteger(parsedBillingCycleDay) || parsedBillingCycleDay < 1 || parsedBillingCycleDay > 31) {
      setErrorMessage('結帳日需介於 1 到 31 之間');
      return;
    }

    const data: CreateCreditCardRequest = {
      bankName: normalizedBankName,
      cardName: normalizedCardName,
      billingCycleDay: parsedBillingCycleDay,
      note: note.trim() || undefined,
    };

    await onSubmit(data);
  };

  return (
    <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50 p-4">
      <div className="card-dark w-full max-w-lg overflow-hidden flex flex-col max-h-[90vh]">
        <div className="p-6 border-b border-[var(--border-color)] flex justify-between items-center">
          <h2 className="text-xl font-bold text-[var(--text-primary)] flex items-center gap-2">
            <CreditCard className="w-5 h-5 text-[var(--accent-peach)]" />
            {initialData ? '編輯信用卡' : '新增信用卡'}
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
              銀行名稱
            </label>
            <input
              type="text"
              value={bankName}
              onChange={(e) => setBankName(e.target.value)}
              className="input-dark w-full"
              maxLength={100}
              required
            />
          </div>

          <div>
            <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
              卡片名稱
            </label>
            <input
              type="text"
              value={cardName}
              onChange={(e) => setCardName(e.target.value)}
              className="input-dark w-full"
              maxLength={100}
              required
            />
          </div>

          <div>
            <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
              結帳日
            </label>
            <select
              value={billingCycleDay}
              onChange={(e) => setBillingCycleDay(e.target.value)}
              className="input-dark w-full"
              required
            >
              {Array.from({ length: 31 }, (_, idx) => idx + 1).map((day) => (
                <option key={day} value={day}>
                  每月 {day} 日
                </option>
              ))}
            </select>
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
