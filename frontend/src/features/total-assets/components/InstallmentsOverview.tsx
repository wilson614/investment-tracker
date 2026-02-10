import { useMemo } from 'react';
import { AlertCircle, ArrowRight, CreditCard } from 'lucide-react';
import { Link } from 'react-router-dom';
import { Skeleton } from '../../../components/common/SkeletonLoader';
import { formatCurrency } from '../../../utils/currency';
import { getErrorMessage } from '../../../utils/errorMapping';
import { useInstallments } from '../../credit-cards/hooks/useInstallments';

export function InstallmentsOverview() {
  const { installments, isLoading, error, refetch } = useInstallments({ status: 'Active' });

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
        <Skeleton width="w-28" height="h-5" />
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
        <p className="mt-2 text-xs text-[var(--text-muted)]">共 {installments.length} 筆進行中分期</p>
      </article>

      <Link
        to="/credit-cards"
        className="text-sm text-[var(--text-muted)] hover:text-[var(--accent-peach)] transition-colors flex items-center gap-1"
      >
        前往信用卡頁面
        <ArrowRight size={14} />
      </Link>
    </section>
  );
}
