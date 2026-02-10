import { useState } from 'react';
import { ChevronDown, ChevronUp, Lock } from 'lucide-react';
import { formatCurrency } from '../../../utils/currency';
import type { TotalAssetsSummary } from '../types';

interface FixedDepositItem {
  id: string;
  bankName: string;
  principal: number;
  currency: string;
  maturityDate?: string;
}

export interface NonDisposableAssetsSectionProps extends Pick<TotalAssetsSummary, 'nonDisposableDeposit'> {
  allocationCount: number;
  fixedDeposits?: FixedDepositItem[];
}

export function NonDisposableAssetsSection({
  nonDisposableDeposit,
  allocationCount,
  fixedDeposits,
}: NonDisposableAssetsSectionProps) {
  const [isFixedDepositsExpanded, setIsFixedDepositsExpanded] = useState(false);

  const handleScrollToAllocations = () => {
    document.getElementById('allocation-management-section')?.scrollIntoView({
      behavior: 'smooth',
      block: 'start',
    });
  };

  const hasFixedDeposits = (fixedDeposits?.length ?? 0) > 0;

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

        {hasFixedDeposits ? (
          <article className="rounded-lg border border-[var(--border-color)] bg-[var(--bg-tertiary)]/30">
            <button
              type="button"
              onClick={() => setIsFixedDepositsExpanded((prev) => !prev)}
              aria-expanded={isFixedDepositsExpanded}
              className="w-full p-4 flex items-center justify-between gap-4 text-left hover:bg-[var(--bg-tertiary)]/40 transition-colors"
            >
              <div className="flex items-center gap-2">
                <Lock className="w-4 h-4 text-[var(--text-muted)]" />
                <div>
                  <p className="text-sm font-medium text-[var(--text-primary)]">定存明細</p>
                  <p className="text-xs text-[var(--text-muted)]">共 {fixedDeposits?.length ?? 0} 筆</p>
                </div>
              </div>
              {isFixedDepositsExpanded ? (
                <ChevronUp className="w-4 h-4 text-[var(--text-muted)]" />
              ) : (
                <ChevronDown className="w-4 h-4 text-[var(--text-muted)]" />
              )}
            </button>

            {isFixedDepositsExpanded ? (
              <div className="px-4 pb-4 space-y-2">
                {fixedDeposits?.map((deposit) => (
                  <div
                    key={deposit.id}
                    className="rounded-lg border border-[var(--border-color)] p-3 bg-[var(--bg-secondary)]"
                  >
                    <div className="flex items-center justify-between gap-3">
                      <p className="text-sm font-medium text-[var(--text-primary)]">{deposit.bankName}</p>
                      <p className="text-sm font-mono text-[var(--text-primary)]">
                        {formatCurrency(deposit.principal, deposit.currency)}
                      </p>
                    </div>
                    <p className="mt-1 text-xs text-[var(--text-muted)]">
                      到期日：{deposit.maturityDate ? deposit.maturityDate.slice(0, 10) : '未提供'}
                    </p>
                  </div>
                ))}
              </div>
            ) : null}
          </article>
        ) : null}
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
