import { Skeleton } from '../../../components/common/SkeletonLoader';
import { formatCurrency } from '../../../utils/currency';
import type { TotalAssetsSummary } from '../types';

interface TotalAssetsBannerProps {
  data?: TotalAssetsSummary;
  isLoading: boolean;
}

export function TotalAssetsBanner({ data, isLoading }: TotalAssetsBannerProps) {
  if (isLoading) {
    return (
      <div className="card-dark p-6 flex flex-col items-center justify-center space-y-2">
        <Skeleton width="w-32" height="h-4" />
        <Skeleton width="w-48" height="h-10" />
      </div>
    );
  }

  const grandTotal = data?.grandTotal ?? 0;
  const bankTotal = data?.bankTotal ?? 0;
  const allocatedTotal = data?.totalAllocated ?? 0;
  const unallocatedAmount = data?.unallocated ?? bankTotal - allocatedTotal;
  const allocationBreakdown = data?.allocationBreakdown ?? [];

  return (
    <div className="card-dark p-8 bg-gradient-to-br from-[var(--bg-secondary)] to-[var(--bg-tertiary)] border-[var(--border-color)] space-y-5">
      <div className="flex flex-col items-center justify-center">
        <h2 className="text-[var(--text-muted)] text-sm uppercase tracking-wider mb-2">
          總資產淨值
        </h2>
        <div className="text-4xl sm:text-5xl font-bold text-[var(--text-primary)] font-mono tracking-tight">
          NT$ {Math.round(grandTotal).toLocaleString('zh-TW')}
        </div>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
        <div className="rounded-lg border border-[var(--border-color)] p-3 bg-[var(--bg-secondary)]/40">
          <p className="text-xs text-[var(--text-muted)]">銀行總資產</p>
          <p className="text-lg font-semibold text-[var(--text-primary)] mt-1">
            {formatCurrency(bankTotal, 'TWD')}
          </p>
        </div>
        <div className="rounded-lg border border-[var(--border-color)] p-3 bg-[var(--bg-secondary)]/40">
          <p className="text-xs text-[var(--text-muted)]">已配置</p>
          <p className="text-lg font-semibold text-[var(--text-primary)] mt-1">
            {formatCurrency(allocatedTotal, 'TWD')}
          </p>
        </div>
        <div className="rounded-lg border border-[var(--border-color)] p-3 bg-[var(--bg-secondary)]/40">
          <p className="text-xs text-[var(--text-muted)]">未配置</p>
          <p
            className={`text-lg font-semibold mt-1 ${
              unallocatedAmount < 0 ? 'text-[var(--color-danger)]' : 'text-[var(--accent-peach)]'
            }`}
          >
            {formatCurrency(unallocatedAmount, 'TWD')}
          </p>
        </div>
      </div>

      {allocationBreakdown.length > 0 ? (
        <div className="space-y-2">
          <p className="text-sm font-medium text-[var(--text-secondary)]">用途分配</p>
          <div className="space-y-2">
            {allocationBreakdown.map((allocation) => {
              const percentage = bankTotal > 0 ? (allocation.amount / bankTotal) * 100 : 0;
              return (
                <div key={allocation.purpose} className="space-y-1">
                  <div className="flex items-center justify-between text-sm">
                    <span className="text-[var(--text-secondary)]">{allocation.purposeDisplayName}</span>
                    <span className="text-[var(--text-primary)] font-medium">
                      {formatCurrency(allocation.amount, 'TWD')} ({percentage.toFixed(1)}%)
                    </span>
                  </div>
                  <div className="w-full h-2 bg-[var(--bg-tertiary)] rounded-full overflow-hidden">
                    <div
                      className="h-full rounded-full bg-[var(--accent-peach)]"
                      style={{ width: `${Math.min(Math.max(percentage, 0), 100)}%` }}
                    />
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      ) : null}
    </div>
  );
}
