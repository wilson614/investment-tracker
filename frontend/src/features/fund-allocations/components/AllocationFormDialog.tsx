import { useEffect, useState, type FormEvent } from 'react';
import { X } from 'lucide-react';

interface AllocationFormDialogSubmitData {
  purpose: string;
  amount: number;
  isDisposable: boolean;
}

interface AllocationFormDialogInitialData {
  purpose: string;
  amount: number;
  isDisposable?: boolean;
}

interface AllocationFormDialogProps {
  isOpen: boolean;
  onClose: () => void;
  onSubmit: (data: AllocationFormDialogSubmitData) => Promise<void> | void;
  initialData?: AllocationFormDialogInitialData;
}

const DEFAULT_PURPOSE = '';

function resolvePurpose(value?: string): string {
  return value ?? DEFAULT_PURPOSE;
}

function resolveIsDisposable(value: boolean | undefined): boolean {
  return value ?? false;
}

export function AllocationFormDialog({ isOpen, onClose, onSubmit, initialData }: AllocationFormDialogProps) {
  const [purpose, setPurpose] = useState<string>(DEFAULT_PURPOSE);
  const [amount, setAmount] = useState<string>('');
  const [isDisposable, setIsDisposable] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    if (!isOpen) return;

    setPurpose(resolvePurpose(initialData?.purpose));
    setAmount(initialData?.amount != null ? String(initialData.amount) : '');
    setIsDisposable(resolveIsDisposable(initialData?.isDisposable));
    setErrorMessage('');
  }, [isOpen, initialData]);

  if (!isOpen) return null;

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setErrorMessage('');

    const normalizedPurpose = purpose.trim();

    if (!normalizedPurpose) {
      setErrorMessage('請輸入用途');
      return;
    }

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
        purpose: normalizedPurpose,
        amount: parsedAmount,
        isDisposable,
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
              <input
                type="text"
                value={purpose}
                onChange={(event) => setPurpose(event.target.value)}
                className="input-dark w-full"
                maxLength={50}
                required
              />
            </div>

            <label className="inline-flex items-center gap-2 text-sm text-[var(--text-secondary)] select-none">
              <input
                type="checkbox"
                checked={isDisposable}
                onChange={(event) => setIsDisposable(event.target.checked)}
                className="checkbox-dark"
                disabled={isSubmitting}
              />
              <span>可動用資金</span>
            </label>

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
