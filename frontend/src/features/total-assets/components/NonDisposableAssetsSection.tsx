import { formatCurrency } from '../../../utils/currency';
import type { FundAllocation } from '../../fund-allocations/types';
import type { TotalAssetsSummary } from '../types';

export interface NonDisposableAssetsSectionProps extends Pick<TotalAssetsSummary, 'nonDisposableDeposit'> {
  nonDisposableAllocations: ReadonlyArray<Pick<FundAllocation, 'id' | 'purposeDisplayName' | 'amount'>>;
}

export function NonDisposableAssetsSection({
  nonDisposableDeposit,
  nonDisposableAllocations,
}: NonDisposableAssetsSectionProps) {
  const subtotal = nonDisposableAllocations.reduce((sum, allocation) => sum + allocation.amount, 0);

  return (
    <section className="card-dark p-6 space-y-5 lg:col-span-1 min-h-[200px] lg:min-h-[280px]">
      <header className="space-y-1">
        <h3 className="text-lg font-semibold text-[var(--text-primary)]">不可動用資產</h3>
        <p className="text-sm text-[var(--text-muted)]">緊急預備金、家庭存款等不納入可投資資金的配置</p>
      </header>

      <div className="rounded-lg border border-[var(--border-color)] p-4 bg-[var(--bg-tertiary)]/50">
        <p className="text-xs uppercase tracking-wider text-[var(--text-muted)]">不可動用存款總額</p>
        <p className="mt-2 text-xl font-semibold font-mono text-[var(--text-primary)]">
          {formatCurrency(nonDisposableDeposit, 'TWD')}
        </p>
      </div>

      <div className="space-y-2">
        <p className="text-xs uppercase tracking-wider text-[var(--text-muted)]">配置明細</p>

        {nonDisposableAllocations.length === 0 ? (
          <div className="text-sm text-[var(--text-muted)] border border-dashed border-[var(--border-color)] rounded-lg p-4 text-center">
            尚無不可動用配置
          </div>
        ) : (
          <>
            {nonDisposableAllocations.map((allocation) => (
              <article
                key={allocation.id}
                className="rounded-lg border border-[var(--border-color)] p-3 bg-[var(--bg-secondary)]/40"
              >
                <div className="flex items-start justify-between gap-3">
                  <p className="text-sm text-[var(--text-secondary)]">{allocation.purposeDisplayName}</p>
                  <p className="text-sm font-semibold font-mono text-[var(--text-primary)]">
                    {formatCurrency(allocation.amount, 'TWD')}
                  </p>
                </div>
              </article>
            ))}

            <div className="rounded-lg border border-[var(--border-color)] p-3 bg-[var(--bg-tertiary)]/50">
              <div className="flex items-center justify-between gap-3">
                <p className="text-sm font-medium text-[var(--text-secondary)]">小計</p>
                <p className="text-sm font-semibold font-mono text-[var(--text-primary)]">
                  {formatCurrency(subtotal, 'TWD')}
                </p>
              </div>
            </div>
          </>
        )}
      </div>
    </section>
  );
}
