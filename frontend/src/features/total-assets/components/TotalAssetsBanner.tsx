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
  const investmentTotal = data?.investmentTotal ?? 0;
  const hasInvestmentTotal = data?.investmentTotal !== undefined;

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

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <div className="rounded-lg border border-[var(--border-color)] p-3 bg-[var(--bg-secondary)]/40">
          <p className="text-xs text-[var(--text-muted)]">銀行總資產</p>
          <p className="text-lg font-semibold text-[var(--text-primary)] mt-1">
            {formatCurrency(bankTotal, 'TWD')}
          </p>
        </div>
        {hasInvestmentTotal ? (
          <div className="rounded-lg border border-[var(--border-color)] p-3 bg-[var(--bg-secondary)]/40">
            <p className="text-xs text-[var(--text-muted)]">投資總額</p>
            <p className="text-lg font-semibold text-[var(--text-primary)] mt-1">
              {formatCurrency(investmentTotal, 'TWD')}
            </p>
          </div>
        ) : null}
      </div>
    </div>
  );
}
