import { Edit, Trash2 } from 'lucide-react';
import type { BankAccount } from '../types';

interface BankAccountCardProps {
  account: BankAccount;
  onEdit: (account: BankAccount) => void;
  onDelete: (id: string) => void;
}

export function BankAccountCard({ account, onEdit, onDelete }: BankAccountCardProps) {
  const formatNumber = (value: number | null | undefined, decimals = 2) => {
    if (value == null) return '-';
    return value.toLocaleString('zh-TW', {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    });
  };

  const formatCurrency = (value: number) => {
    return Math.round(value).toLocaleString('zh-TW'); // TWD usually no decimals
  };

  return (
    <div className="card-dark p-5 hover:border-[var(--border-hover)] transition-all group relative">
      <div className="absolute top-4 right-4 flex gap-2 opacity-0 group-hover:opacity-100 transition-opacity">
        <button
          onClick={() => onEdit(account)}
          className="p-1.5 text-[var(--text-muted)] hover:text-[var(--accent-blue)] hover:bg-[var(--bg-tertiary)] rounded transition-colors"
          title="編輯"
        >
          <Edit size={16} />
        </button>
        <button
          onClick={() => onDelete(account.id)}
          className="p-1.5 text-[var(--text-muted)] hover:text-[var(--color-danger)] hover:bg-[var(--bg-tertiary)] rounded transition-colors"
          title="刪除"
        >
          <Trash2 size={16} />
        </button>
      </div>

      <div className="mb-4">
        <h3 className="text-xl font-bold text-[var(--accent-cream)] mb-1">{account.bankName}</h3>
        {account.note && (
          <p className="text-sm text-[var(--text-muted)] line-clamp-1">{account.note}</p>
        )}
      </div>

      <div className="mb-6">
        <p className="text-sm text-[var(--text-muted)] mb-1">總資產</p>
        <p className="text-2xl font-bold text-[var(--text-primary)] number-display">
          {formatCurrency(account.totalAssets)} <span className="text-sm font-normal text-[var(--text-muted)]">TWD</span>
        </p>
      </div>

      <div className="grid grid-cols-2 gap-4 text-sm">
        <div>
          <p className="text-[var(--text-muted)] mb-1">活存利率</p>
          <div className="flex items-center gap-2">
            <span className="font-medium text-[var(--accent-teal)] number-display">
              {formatNumber(account.interestRate)}%
            </span>
            <span className="text-xs text-[var(--text-muted)]">
              (上限 {account.interestCap ? formatCurrency(account.interestCap) : '無上限'})
            </span>
          </div>
        </div>
        <div>
          <p className="text-[var(--text-muted)] mb-1">預估月息</p>
          <p className="font-medium text-[var(--accent-peach)] number-display">
            {formatNumber(account.monthlyInterest, 0)} <span className="text-xs text-[var(--text-muted)]">TWD</span>
          </p>
        </div>
      </div>
    </div>
  );
}
