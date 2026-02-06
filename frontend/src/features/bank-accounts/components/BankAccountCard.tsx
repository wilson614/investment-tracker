import { useEffect, useState } from 'react';
import { AlertTriangle, Edit, Trash2 } from 'lucide-react';
import { formatCurrency } from '../../../utils/currency';
import type { BankAccount } from '../types';

const EXCHANGE_RATE_STALE_THRESHOLD_MS = 24 * 60 * 60 * 1000;

interface BankAccountCardProps {
  account: BankAccount;
  onEdit: (account: BankAccount) => void;
  onDelete: (id: string) => void;
}

export function BankAccountCard({ account, onEdit, onDelete }: BankAccountCardProps) {
  const [currentTime, setCurrentTime] = useState(() => Date.now());

  const formatNumber = (value: number | null | undefined, decimals = 2) => {
    if (value == null) return '-';
    return value.toLocaleString('zh-TW', {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    });
  };

  useEffect(() => {
    setCurrentTime(Date.now());

    if (account.currency === 'TWD') {
      return;
    }

    const timerId = window.setInterval(() => {
      setCurrentTime(Date.now());
    }, 60 * 1000);

    return () => {
      window.clearInterval(timerId);
    };
  }, [account.currency, account.updatedAt]);

  const isForeignCurrency = account.currency !== 'TWD';
  // TODO: Replace with dedicated exchange-rate timestamp once backend provides it.
  const lastUpdatedTime = new Date(account.updatedAt).getTime();
  const isExchangeRateStale =
    isForeignCurrency && Number.isFinite(lastUpdatedTime) && currentTime - lastUpdatedTime > EXCHANGE_RATE_STALE_THRESHOLD_MS;

  return (
    <div className="card-dark p-5 hover:border-[var(--border-hover)] transition-all group relative">
      <div className="absolute top-4 right-4 flex gap-2 opacity-0 group-hover:opacity-100 transition-opacity">
        <button
          onClick={() => onEdit(account)}
          className="p-1.5 text-[var(--text-muted)] hover:text-[var(--accent-peach)] hover:bg-[var(--bg-tertiary)] rounded transition-colors"
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

      {isExchangeRateStale && (
        <div className="mb-4 inline-flex items-center gap-1 text-xs text-[var(--color-warning)]">
          <AlertTriangle className="w-3.5 h-3.5" />
          <span>匯率可能已過時</span>
        </div>
      )}

      <div className="mb-6">
        <p className="text-sm text-[var(--text-muted)] mb-1">總資產</p>
        <p className="text-2xl font-bold text-[var(--text-primary)] number-display">
          {formatCurrency(account.totalAssets, account.currency)}{' '}
          <span className="text-sm font-normal text-[var(--text-muted)]">{account.currency}</span>
        </p>
      </div>

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
    </div>
  );
}
