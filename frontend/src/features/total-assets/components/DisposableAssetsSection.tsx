import { ArrowRight } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { formatCurrency } from '../../../utils/currency';
import type { TotalAssetsSummary } from '../types';

export interface DisposableAssetsSectionProps
  extends Pick<TotalAssetsSummary, 'portfolioValue' | 'cashBalance' | 'disposableDeposit' | 'investmentTotal'> {}

export function DisposableAssetsSection({
  portfolioValue,
  cashBalance,
  disposableDeposit,
  investmentTotal,
}: DisposableAssetsSectionProps) {
  const navigate = useNavigate();

  return (
    <section className="card-dark p-6 space-y-5 lg:col-span-2">
      <header className="space-y-1">
        <h3 className="text-lg font-semibold text-[var(--text-primary)]">可動用資產</h3>
        <p className="text-sm text-[var(--text-muted)]">聚焦可動用存款與投資部位，追蹤資金配置效率</p>
      </header>

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
        <button
          type="button"
          onClick={() => navigate('/portfolio')}
          className="rounded-lg border border-[var(--border-color)] p-4 bg-[var(--bg-tertiary)]/50 text-left transition-colors hover:border-[var(--accent-peach)]/40 hover:bg-[var(--bg-tertiary)]/70 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--accent-peach)]/40"
        >
          <div className="flex items-start justify-between gap-2">
            <p className="text-xs uppercase tracking-wider text-[var(--text-muted)]">投資部位總額</p>
            <ArrowRight size={16} className="mt-0.5 shrink-0 text-[var(--text-muted)]" />
          </div>
          <p className="mt-2 text-xl font-semibold font-mono text-[var(--text-primary)]">
            {formatCurrency(investmentTotal, 'TWD')}
          </p>
          <p className="mt-1 text-xs text-[var(--text-muted)]">
            股票市值 {formatCurrency(portfolioValue, 'TWD')} + 帳本現金 {formatCurrency(cashBalance, 'TWD')}
          </p>
          <p className="mt-2 text-xs text-[var(--accent-peach)]">前往投資組合</p>
        </button>

        <article className="rounded-lg border border-[var(--border-color)] p-4 bg-[var(--bg-tertiary)]/50">
          <p className="text-xs uppercase tracking-wider text-[var(--text-muted)]">帳本現金</p>
          <p className="mt-2 text-xl font-semibold font-mono text-[var(--text-primary)]">
            {formatCurrency(cashBalance, 'TWD')}
          </p>
        </article>

        <article className="rounded-lg border border-[var(--border-color)] p-4 bg-[var(--bg-tertiary)]/50">
          <p className="text-xs uppercase tracking-wider text-[var(--text-muted)]">可動用存款</p>
          <p className="mt-2 text-xl font-semibold font-mono text-[var(--text-primary)]">
            {formatCurrency(disposableDeposit, 'TWD')}
          </p>
        </article>
      </div>
    </section>
  );
}
