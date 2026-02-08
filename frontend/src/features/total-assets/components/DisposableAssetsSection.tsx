import { ArrowRight } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { formatCurrency } from '../../../utils/currency';
import type { TotalAssetsSummary } from '../types';

export type DisposableAssetsSectionProps =
  Pick<TotalAssetsSummary, 'cashBalance' | 'disposableDeposit' | 'investmentTotal'>;

export function DisposableAssetsSection({
  cashBalance,
  disposableDeposit,
  investmentTotal,
}: DisposableAssetsSectionProps) {
  const navigate = useNavigate();

  // investmentTotal 從後端來的是股票市值，不是投資部位總額
  const portfolioMarketValue = investmentTotal - cashBalance;

  return (
    <section className="card-dark p-6 space-y-5 min-h-[200px] lg:h-[280px] h-full flex flex-col">
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
            <p className="text-sm uppercase tracking-wide text-[var(--text-muted)]">股票市值</p>
            <ArrowRight size={16} className="mt-0.5 shrink-0 text-[var(--text-muted)]" />
          </div>
          <p className="mt-2 text-xl font-semibold font-mono text-[var(--text-primary)]">
            {formatCurrency(portfolioMarketValue, 'TWD')}
          </p>
          <p className="mt-2 text-sm text-[var(--accent-peach)]">前往投資組合</p>
        </button>

        <button
          type="button"
          onClick={() => navigate('/ledger')}
          className="rounded-lg border border-[var(--border-color)] p-4 bg-[var(--bg-tertiary)]/50 text-left transition-colors hover:border-[var(--accent-peach)]/40 hover:bg-[var(--bg-tertiary)]/70 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--accent-peach)]/40"
        >
          <div className="flex items-start justify-between gap-2">
            <p className="text-sm uppercase tracking-wide text-[var(--text-muted)]">帳本現金</p>
            <ArrowRight size={16} className="mt-0.5 shrink-0 text-[var(--text-muted)]" />
          </div>
          <p className="mt-2 text-xl font-semibold font-mono text-[var(--text-primary)]">
            {formatCurrency(cashBalance, 'TWD')}
          </p>
          <p className="mt-2 text-sm text-[var(--accent-peach)]">前往帳本</p>
        </button>

        <button
          type="button"
          onClick={() => navigate('/bank-accounts')}
          className="rounded-lg border border-[var(--border-color)] p-4 bg-[var(--bg-tertiary)]/50 text-left transition-colors hover:border-[var(--accent-peach)]/40 hover:bg-[var(--bg-tertiary)]/70 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--accent-peach)]/40"
        >
          <div className="flex items-start justify-between gap-2">
            <p className="text-sm uppercase tracking-wide text-[var(--text-muted)]">可動用存款</p>
            <ArrowRight size={16} className="mt-0.5 shrink-0 text-[var(--text-muted)]" />
          </div>
          <p className="mt-2 text-xl font-semibold font-mono text-[var(--text-primary)]">
            {formatCurrency(disposableDeposit, 'TWD')}
          </p>
          <p className="mt-2 text-sm text-[var(--accent-peach)]">前往銀行帳戶</p>
        </button>
      </div>
    </section>
  );
}
