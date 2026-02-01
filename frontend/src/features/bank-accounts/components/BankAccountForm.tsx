import { useState, useEffect } from 'react';
import { X, Save, Building2 } from 'lucide-react';
import type { BankAccount, CreateBankAccountRequest, UpdateBankAccountRequest } from '../types';

interface BankAccountFormProps {
  initialData?: BankAccount;
  onSubmit: (data: CreateBankAccountRequest | UpdateBankAccountRequest) => Promise<boolean>;
  onCancel: () => void;
  isLoading: boolean;
}

export function BankAccountForm({ initialData, onSubmit, onCancel, isLoading }: BankAccountFormProps) {
  const [bankName, setBankName] = useState('');
  const [totalAssets, setTotalAssets] = useState<string>('');
  const [interestRate, setInterestRate] = useState<string>('');
  const [interestCap, setInterestCap] = useState<string>('');
  const [note, setNote] = useState('');

  useEffect(() => {
    if (initialData) {
      setBankName(initialData.bankName);
      setTotalAssets(initialData.totalAssets.toString());
      setInterestRate(initialData.interestRate.toString());
      setInterestCap(initialData.interestCap !== undefined ? initialData.interestCap.toString() : '');
      setNote(initialData.note || '');
    }
  }, [initialData]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    const assets = parseFloat(totalAssets);
    const rate = parseFloat(interestRate);
    const cap = interestCap === '' ? undefined : parseFloat(interestCap);

    if (!bankName || isNaN(assets) || isNaN(rate) || (cap !== undefined && isNaN(cap))) {
      return;
    }

    const data: CreateBankAccountRequest = {
      bankName,
      totalAssets: assets,
      interestRate: rate,
      interestCap: cap,
      note: note.trim() || undefined
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
              placeholder="例如：台新 Richart"
            />
          </div>

          <div>
            <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
              總資產（TWD）
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

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                年利率（%）
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
                優惠上限（TWD）
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
              <p className="text-xs text-[var(--text-muted)] mt-1">留空表示無優惠上限</p>
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
              placeholder="關於此帳戶的備註..."
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
