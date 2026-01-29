import { Pencil, Trash2, Building2 } from 'lucide-react';
import type { BankAccount } from '../../types';

interface BankAccountCardProps {
  account: BankAccount;
  onEdit: (account: BankAccount) => void;
  onDelete: (id: string) => void;
}

export function BankAccountCard({ account, onEdit, onDelete }: BankAccountCardProps) {
  const formatCurrency = (val: number) => {
    return new Intl.NumberFormat('zh-TW', {
      style: 'currency',
      currency: 'TWD',
      maximumFractionDigits: 0
    }).format(val);
  };

  return (
    <div className="card-dark p-6 transition-all duration-200 hover:border-[var(--accent-peach)]">
      <div className="flex justify-between items-start mb-4">
        <div className="flex items-center gap-3">
          <div className="p-3 bg-[var(--bg-primary)] rounded-full">
            <Building2 className="w-6 h-6 text-[var(--accent-peach)]" />
          </div>
          <div>
            <h3 className="text-xl font-bold text-[var(--text-primary)]">{account.bankName}</h3>
            {account.note && (
              <p className="text-sm text-[var(--text-secondary)] mt-0.5">{account.note}</p>
            )}
          </div>
        </div>
        <div className="flex gap-2">
          <button
            onClick={() => onEdit(account)}
            className="p-2 text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-[var(--bg-hover)] rounded-lg transition-colors"
            title="編輯"
          >
            <Pencil className="w-4 h-4" />
          </button>
          <button
            onClick={() => onDelete(account.id)}
            className="p-2 text-[var(--text-secondary)] hover:text-[var(--color-danger)] hover:bg-[var(--bg-hover)] rounded-lg transition-colors"
            title="刪除"
          >
            <Trash2 className="w-4 h-4" />
          </button>
        </div>
      </div>

      <div className="space-y-4">
        <div>
          <p className="text-sm text-[var(--text-secondary)] mb-1">總資產</p>
          <p className="text-2xl font-bold text-[var(--text-primary)] tracking-tight">
            {formatCurrency(account.totalAssets)}
          </p>
        </div>

        <div className="grid grid-cols-2 gap-4 pt-4 border-t border-[var(--border-color)]">
          <div>
            <p className="text-sm text-[var(--text-secondary)] mb-1">年利率</p>
            <p className="text-lg font-semibold text-[var(--color-success)]">
              {account.interestRate}%
              {account.interestCap > 0 && (
                <span className="text-xs text-[var(--text-muted)] ml-1 font-normal">
                  (上限 {formatCurrency(account.interestCap)})
                </span>
              )}
            </p>
          </div>
          <div>
            <p className="text-sm text-[var(--text-secondary)] mb-1">預估年息</p>
            <p className="text-lg font-semibold text-[var(--accent-butter)]">
              {formatCurrency(account.yearlyInterest)}
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}
