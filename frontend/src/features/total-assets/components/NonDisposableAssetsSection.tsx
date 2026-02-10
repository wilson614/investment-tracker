import { formatCurrency } from '../../../utils/currency';
import type { TotalAssetsSummary } from '../types';

export interface NonDisposableAssetsSectionProps extends Pick<TotalAssetsSummary, 'nonDisposableDeposit'> {
  allocationCount: number;
}

export function NonDisposableAssetsSection({
  nonDisposableDeposit,
  allocationCount,
}: NonDisposableAssetsSectionProps) {
  const handleScrollToAllocations = () => {
    document.getElementById('allocation-management-section')?.scrollIntoView({
      behavior: 'smooth',
      block: 'start',
    });
  };

  return (
    <section className="card-dark p-6 min-h-[200px] lg:h-[280px] h-full flex flex-col">
      <header className="space-y-1">
        <h3 className="text-lg font-semibold text-[var(--text-primary)]">不可動用資產</h3>
        <p className="text-sm text-[var(--text-muted)]">緊急預備金、家庭存款等不納入可動用資產</p>
      </header>

      <div className="flex-1 mt-4 space-y-3">
        <div className="rounded-lg border border-[var(--border-color)] p-4 bg-[var(--bg-tertiary)]/50 space-y-2">
          <p className="text-sm uppercase tracking-wide text-[var(--text-muted)]">不可動用存款總額</p>
          <p className="text-xl font-semibold font-mono text-[var(--text-primary)]">
            {formatCurrency(nonDisposableDeposit, 'TWD')}
          </p>
          <p className="text-sm text-[var(--text-muted)]">共 {allocationCount} 筆配置</p>
        </div>
      </div>

      <button
        type="button"
        onClick={handleScrollToAllocations}
        className="mt-4 self-start text-sm font-medium text-[var(--accent-peach)] hover:opacity-90 transition-opacity"
      >
        查看配置明細 →
      </button>
    </section>
  );
}
