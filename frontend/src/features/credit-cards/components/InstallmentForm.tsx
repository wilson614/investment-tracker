import { useEffect, useState } from 'react';
import { X, Save, CalendarRange } from 'lucide-react';
import type {
  CreateInstallmentRequest,
  InstallmentResponse,
  UpdateInstallmentRequest,
} from '../types';

interface InstallmentFormProps {
  creditCardId: string;
  initialData?: InstallmentResponse;
  onSubmit: (data: CreateInstallmentRequest | UpdateInstallmentRequest) => Promise<boolean>;
  onCancel: () => void;
  isLoading: boolean;
}

export function InstallmentForm({
  creditCardId,
  initialData,
  onSubmit,
  onCancel,
  isLoading,
}: InstallmentFormProps) {
  const [description, setDescription] = useState('');
  const [totalAmount, setTotalAmount] = useState('');
  const [numberOfInstallments, setNumberOfInstallments] = useState('3');
  const [firstPaymentDate, setFirstPaymentDate] = useState('');
  const [note, setNote] = useState('');
  const [errorMessage, setErrorMessage] = useState('');

  useEffect(() => {
    if (initialData) {
      setDescription(initialData.description);
      setTotalAmount(initialData.totalAmount.toString());
      setNumberOfInstallments(initialData.numberOfInstallments.toString());
      setFirstPaymentDate(initialData.firstPaymentDate.slice(0, 10));
      setNote(initialData.note ?? '');
    } else {
      setDescription('');
      setTotalAmount('');
      setNumberOfInstallments('3');
      setFirstPaymentDate(new Date().toISOString().slice(0, 10));
      setNote('');
    }
    setErrorMessage('');
  }, [initialData]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErrorMessage('');

    const normalizedDescription = description.trim();
    const parsedTotalAmount = Number(totalAmount);
    const parsedInstallments = Number(numberOfInstallments);

    if (!normalizedDescription) {
      setErrorMessage('分期項目為必填');
      return;
    }

    if (!Number.isFinite(parsedTotalAmount) || parsedTotalAmount <= 0) {
      setErrorMessage('總金額需大於 0');
      return;
    }

    if (!Number.isInteger(parsedInstallments) || parsedInstallments < 2) {
      setErrorMessage('期數需為至少 2 期的整數');
      return;
    }

    if (!firstPaymentDate) {
      setErrorMessage('請選擇首次還款日');
      return;
    }

    if (initialData) {
      const updatePayload: UpdateInstallmentRequest = {
        description: normalizedDescription,
        note: note.trim() || undefined,
      };

      await onSubmit(updatePayload);
      return;
    }

    const createPayload: CreateInstallmentRequest = {
      creditCardId,
      description: normalizedDescription,
      totalAmount: parsedTotalAmount,
      numberOfInstallments: parsedInstallments,
      firstPaymentDate,
      note: note.trim() || undefined,
    };

    await onSubmit(createPayload);
  };

  return (
    <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50 p-4">
      <div className="card-dark w-full max-w-lg overflow-hidden flex flex-col max-h-[90vh]">
        <div className="p-6 border-b border-[var(--border-color)] flex justify-between items-center">
          <h2 className="text-xl font-bold text-[var(--text-primary)] flex items-center gap-2">
            <CalendarRange className="w-5 h-5 text-[var(--accent-peach)]" />
            {initialData ? '編輯分期' : '新增分期'}
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
              分期項目
            </label>
            <input
              type="text"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              className="input-dark w-full"
              maxLength={200}
              required
            />
          </div>

          {!initialData ? (
            <div>
              <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                總金額
              </label>
              <input
                type="number"
                value={totalAmount}
                onChange={(e) => setTotalAmount(e.target.value)}
                className="input-dark w-full"
                min="0"
                step="1"
                required
              />
            </div>
          ) : (
            <div>
              <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                總金額
              </label>
              <input
                type="number"
                value={totalAmount}
                className="input-dark w-full opacity-70 cursor-not-allowed"
                disabled
              />
              <p className="text-xs text-[var(--text-muted)] mt-1">編輯模式不可變更金額</p>
            </div>
          )}

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {!initialData ? (
              <div>
                <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                  分期期數
                </label>
                <input
                  type="number"
                  value={numberOfInstallments}
                  onChange={(e) => setNumberOfInstallments(e.target.value)}
                  className="input-dark w-full"
                  min="2"
                  step="1"
                  required
                />
              </div>
            ) : (
              <div>
                <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                  分期期數
                </label>
                <input
                  type="number"
                  value={numberOfInstallments}
                  className="input-dark w-full opacity-70 cursor-not-allowed"
                  disabled
                />
              </div>
            )}

            {!initialData ? (
              <div>
                <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                  首次還款日
                </label>
                <input
                  type="date"
                  value={firstPaymentDate}
                  onChange={(e) => setFirstPaymentDate(e.target.value)}
                  className="input-dark w-full"
                  required
                />
              </div>
            ) : (
              <div>
                <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                  首次還款日
                </label>
                <input
                  type="date"
                  value={firstPaymentDate}
                  className="input-dark w-full opacity-70 cursor-not-allowed"
                  disabled
                />
              </div>
            )}
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
