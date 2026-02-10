import { Edit, Trash2, Timer, CheckCircle2, XCircle } from 'lucide-react';
import { formatCurrency } from '../../../utils/currency';
import type { BankAccount } from '../types';

interface BankAccountCardProps {
  account: BankAccount;
  onEdit: (account: BankAccount) => void;
  onDelete: (id: string) => void;
  showCurrencyBadge?: boolean;
}

const FIXED_DEPOSIT_STATUS_LABEL: Record<NonNullable<BankAccount['fixedDepositStatus']>, string> = {
  Active: '進行中',
  Matured: '已到期',
  Closed: '已結清',
  EarlyWithdrawal: '提前解約',
};

const FIXED_DEPOSIT_STATUS_CLASS: Record<NonNullable<BankAccount['fixedDepositStatus']>, string> = {
  Active: 'border-[var(--accent-butter)]/40 bg-[var(--accent-butter)]/10 text-[var(--accent-butter)]',
  Matured: 'border-[var(--color-warning)]/40 bg-[var(--color-warning)]/10 text-[var(--color-warning)]',
  Closed: 'border-[var(--color-success)]/40 bg-[var(--color-success)]/10 text-[var(--color-success)]',
  EarlyWithdrawal: 'border-[var(--color-danger)]/40 bg-[var(--color-danger)]/10 text-[var(--color-danger)]',
};

export function BankAccountCard({ account, onEdit, onDelete, showCurrencyBadge = true }: BankAccountCardProps) {
  const formatNumber = (value: number | null | undefined, decimals = 2) => {
    if (value == null) return '-';
    return value.toLocaleString('zh-TW', {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    });
  };

  const isFixedDeposit = account.accountType === 'FixedDeposit';
  const fixedDepositStatus = account.fixedDepositStatus ?? 'Active';
  const statusLabel = FIXED_DEPOSIT_STATUS_LABEL[fixedDepositStatus];
  const statusClass = FIXED_DEPOSIT_STATUS_CLASS[fixedDepositStatus];

  return (
    <div className="card-dark p-5 hover:border-[var(--border-hover)] transition-all group relative">
      <div className="absolute bottom-4 right-4 flex gap-2 opacity-0 group-hover:opacity-100 transition-opacity z-10">
        <button type="button"
          onClick={() => onEdit(account)}
          className="p-1.5 text-[var(--text-muted)] hover:text-[var(--accent-peach)] hover:bg-[var(--bg-tertiary)] rounded transition-colors"
          title="編輯"
        >
          <Edit size={16} />
        </button>
        <button type="button"
          onClick={() => onDelete(account.id)}
          className="p-1.5 text-[var(--text-muted)] hover:text-[var(--color-danger)] hover:bg-[var(--bg-tertiary)] rounded transition-colors"
          title="刪除"
        >
          <Trash2 size={16} />
        </button>
      </div>

      <div className="mb-4">
        <div className="mb-1 flex items-center justify-between gap-2">
          <h3 className="text-xl font-bold text-[var(--accent-cream)]">{account.bankName}</h3>
          <div className="flex items-center gap-2">
            {showCurrencyBadge && (
              <span className="inline-flex items-center rounded-full border border-[var(--border-color)] bg-[var(--bg-secondary)] px-2 py-0.5 text-xs font-medium text-[var(--text-secondary)]">
                {account.currency}
              </span>
            )}
            <span
              className={`inline-flex items-center rounded-full border px-2 py-0.5 text-xs font-medium ${
                isFixedDeposit
                  ? 'border-[var(--accent-butter)]/40 bg-[var(--accent-butter)]/10 text-[var(--accent-butter)]'
                  : 'border-[var(--accent-peach)]/40 bg-[var(--accent-peach)]/10 text-[var(--accent-peach)]'
              }`}
            >
              {isFixedDeposit ? '定存' : '活存'}
            </span>
          </div>
        </div>
        {account.note && (
          <p className="text-sm text-[var(--text-muted)] line-clamp-1">{account.note}</p>
        )}
      </div>

      {isFixedDeposit && fixedDepositStatus !== 'Active' && (
        <div className={`mb-4 inline-flex items-center gap-1 rounded-full border px-2 py-1 text-xs font-medium ${statusClass}`}>
          {fixedDepositStatus === 'Closed' ? (
            <CheckCircle2 className="w-3.5 h-3.5" />
          ) : fixedDepositStatus === 'EarlyWithdrawal' ? (
            <XCircle className="w-3.5 h-3.5" />
          ) : (
            <Timer className="w-3.5 h-3.5" />
          )}
          <span>{statusLabel}</span>
        </div>
      )}

      <div className="mb-6">
        <p className="text-sm text-[var(--text-muted)] mb-1">{isFixedDeposit ? '定存本金' : '總資產'}</p>
        <p className="text-2xl font-bold text-[var(--text-primary)] number-display">
          {formatCurrency(account.totalAssets, account.currency)}{' '}
          <span className="text-sm font-normal text-[var(--text-muted)]">{account.currency}</span>
        </p>
      </div>

      {isFixedDeposit ? (
        <div className="space-y-3 text-sm">
          <div className="grid grid-cols-2 gap-4">
            <div>
              <p className="text-[var(--text-muted)] mb-1">定存利率</p>
              <p className="font-medium text-[var(--accent-butter)] number-display">
                {formatNumber(account.interestRate)}%
              </p>
            </div>
            <div>
              <p className="text-[var(--text-muted)] mb-1">定存期數</p>
              <p className="font-medium text-[var(--text-primary)]">{account.termMonths ? `${account.termMonths} 個月` : '-'}</p>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <p className="text-[var(--text-muted)] mb-1">預期利息</p>
              <p className="font-medium text-[var(--accent-butter)] number-display">
                {account.expectedInterest != null ? formatCurrency(account.expectedInterest, account.currency) : '-'}
              </p>
            </div>
            <div>
              <p className="text-[var(--text-muted)] mb-1">實際利息</p>
              <p className="font-medium text-[var(--text-primary)] number-display">
                {account.actualInterest != null ? formatCurrency(account.actualInterest, account.currency) : '-'}
              </p>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <p className="text-[var(--text-muted)] mb-1">起始日</p>
              <p className="font-medium text-[var(--text-primary)]">{account.startDate ? account.startDate.slice(0, 10) : '-'}</p>
            </div>
            <div>
              <p className="text-[var(--text-muted)] mb-1">到期日</p>
              <p className="font-medium text-[var(--text-primary)]">{account.maturityDate ? account.maturityDate.slice(0, 10) : '-'}</p>
            </div>
          </div>

        </div>
      ) : (
        <div className="grid grid-cols-2 gap-4 text-sm">
          <div>
            <p className="text-[var(--text-muted)] mb-1">活存利率</p>
            <p className="font-medium text-[var(--accent-peach)] number-display">
              {formatNumber(account.interestRate)}%
            </p>
            <p className="text-xs text-[var(--text-muted)] mt-1">
              額度上限：{account.interestCap != null ? formatCurrency(account.interestCap, account.currency) : '無上限'}
            </p>
          </div>
          <div>
            <p className="text-[var(--text-muted)] mb-1">預估月息</p>
            <p className="font-medium text-[var(--accent-peach)] number-display">
              {formatCurrency(account.monthlyInterest, account.currency)}{' '}
              <span className="text-xs text-[var(--text-muted)]">{account.currency}</span>
            </p>
          </div>
        </div>
      )}
    </div>
  );
}
