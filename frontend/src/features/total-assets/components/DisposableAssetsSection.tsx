import { ArrowRight, Info } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { formatCurrency } from '../../../utils/currency';
import type { TotalAssetsSummary } from '../types';

export type DisposableAssetsSectionProps =
  Pick<TotalAssetsSummary, 'portfolioValue' | 'cashBalance' | 'disposableDeposit' | 'investmentTotal'>;

export function DisposableAssetsSection({
  
  cashBalance,
  disposableDeposit,
  investmentTotal,
}: DisposableAssetsSectionProps) {
  const navigate = useNavigate();

  return (
    <section className="card-dark p-6 space-y-5 min-h-[200px] lg:min-h-[280px] h-full flex flex-col">
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
            <div className="flex items-center gap-1">
              <p className="text-sm uppercase tracking-wide text-[var(--text-muted)]">投資部位總額</p>
              <div className="relative group">
                <Info
                  className="w-3.5 h-3.5 text-[var(--text-muted)] cursor-help"
                  aria-label="股票市值 + 帳本現金"
                />
                <div className="absolute left-0 bottom-full mb-2 hidden group-hover:block z-10 w-max max-w-72">
                  <div className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg p-2 shadow-lg text-sm text-[var(--text-secondary)] leading-relaxed whitespace-normal">
                    股票市值 + 帳本現金
                  </div>
                </div>
              </div>
            </div>
            <ArrowRight size={16} className="mt-0.5 shrink-0 text-[var(--text-muted)]" />
          </div>
          <p className="mt-2 text-xl font-semibold font-mono text-[var(--text-primary)]">
            {formatCurrency(investmentTotal, 'TWD')}
          </p>
          <p className="mt-2 text-sm text-[var(--accent-peach)]">前往投資組合</p>
        </button>

        <button
          type="button"
          onClick={() => navigate('/currency')}
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
