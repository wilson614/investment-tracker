import { useMemo, useState } from 'react';
import { AlertCircle, ChevronDown, ChevronUp, CreditCard } from 'lucide-react';
import { Skeleton } from '../../../components/common/SkeletonLoader';
import { formatCurrency } from '../../../utils/currency';
import { getErrorMessage } from '../../../utils/errorMapping';
import { useInstallments } from '../../credit-cards/hooks/useInstallments';

export function InstallmentsOverview() {
  const { installments, isLoading, error, refetch } = useInstallments({ status: 'Active' });
  const [isExpanded, setIsExpanded] = useState(false);

  const totalUnpaidBalance = useMemo(
    () => installments.reduce((sum, installment) => sum + installment.unpaidBalance, 0),
    [installments]
  );

  if (isLoading) {
    return (
      <section className="card-dark p-6 space-y-4" aria-label="分期付款總覽載入中">
        <div className="space-y-1">
          <Skeleton width="w-36" height="h-7" />
          <Skeleton width="w-52" height="h-4" />
        </div>
        <div className="rounded-lg border border-[var(--accent-peach)]/40 p-4 bg-[var(--accent-peach)]/10">
          <Skeleton width="w-32" height="h-4" />
          <Skeleton width="w-40" height="h-9" className="mt-2" />
        </div>
        <div className="rounded-lg border border-[var(--border-color)] p-4 bg-[var(--bg-tertiary)]/30">
          <div className="flex items-center justify-between">
            <Skeleton width="w-36" height="h-5" />
            <Skeleton width="w-5" height="h-5" />
          </div>
        </div>
      </section>
    );
  }

  if (error) {
    return (
      <section className="card-dark p-6 space-y-3" role="alert">
        <h3 className="text-lg font-semibold text-[var(--text-primary)]">分期付款總覽</h3>
        <div className="p-3 rounded border border-red-500/40 bg-red-500/10 text-red-200 text-sm">
          分期資料載入失敗：{getErrorMessage(error)}
        </div>
        <button
          type="button"
          onClick={() => void refetch()}
          className="btn-dark px-3 py-1.5 text-sm"
        >
          重試
        </button>
      </section>
    );
  }

  if (installments.length === 0) {
    return (
      <section className="card-dark p-6 space-y-4">
        <h3 className="text-lg font-semibold text-[var(--text-primary)]">分期付款總覽</h3>
        <div className="text-center py-8 bg-[var(--bg-secondary)] rounded-xl border border-dashed border-[var(--border-color)]">
          <AlertCircle className="w-10 h-10 text-[var(--text-muted)] mx-auto mb-3" />
          <p className="text-sm text-[var(--text-muted)]">目前沒有進行中的分期付款。</p>
        </div>
      </section>
    );
  }

  return (
    <section className="card-dark p-6 space-y-4" aria-label="分期付款總覽">
      <header className="space-y-1">
        <h3 className="text-lg font-semibold text-[var(--text-primary)]">分期付款總覽</h3>
        <p className="text-sm text-[var(--text-muted)]">追蹤進行中分期的未繳餘額</p>
      </header>

      <article className="rounded-lg border border-[var(--accent-peach)]/40 p-4 bg-[var(--accent-peach)]/10">
        <div className="flex items-center gap-2 text-[var(--accent-peach)]">
          <CreditCard className="w-4 h-4" />
          <p className="text-sm uppercase tracking-wide font-medium">未繳分期餘額</p>
        </div>
        <p className="mt-2 text-3xl font-bold font-mono text-[var(--text-primary)]">
          {formatCurrency(totalUnpaidBalance, 'TWD')}
        </p>
      </article>

      <article className="rounded-lg border border-[var(--border-color)] bg-[var(--bg-tertiary)]/30">
        <button
          type="button"
          onClick={() => setIsExpanded((prev) => !prev)}
          aria-expanded={isExpanded}
          className="w-full p-4 flex items-center justify-between gap-4 text-left hover:bg-[var(--bg-tertiary)]/40 transition-colors"
        >
          <div>
            <p className="text-sm font-medium text-[var(--text-primary)]">分期明細</p>
            <p className="text-sm text-[var(--text-muted)]">共 {installments.length} 筆進行中分期</p>
          </div>
          {isExpanded ? (
            <ChevronUp className="w-4 h-4 text-[var(--text-muted)]" />
          ) : (
            <ChevronDown className="w-4 h-4 text-[var(--text-muted)]" />
          )}
        </button>

        {isExpanded ? (
          <div className="px-4 pb-4 space-y-2">
            {installments.map((installment) => (
              <div
                key={installment.id}
                className="rounded-lg border border-[var(--border-color)] p-3 bg-[var(--bg-secondary)]"
              >
                <div className="flex items-center justify-between gap-3">
                  <p className="text-sm font-medium text-[var(--text-primary)]">{installment.description}</p>
                  <p className="text-sm font-mono text-[var(--text-primary)]">
                    {formatCurrency(installment.unpaidBalance, 'TWD')}
                  </p>
                </div>
                <p className="mt-1 text-xs text-[var(--text-muted)]">信用卡：{installment.creditCardName}</p>
              </div>
            ))}
          </div>
        ) : null}
      </article>
    </section>
  );
}
