import { Skeleton } from '../../../components/common/SkeletonLoader';
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

  return (
    <div className="card-dark p-8 flex flex-col items-center justify-center bg-gradient-to-br from-[var(--bg-secondary)] to-[var(--bg-tertiary)] border-[var(--border-color)]">
      <h2 className="text-[var(--text-muted)] text-sm uppercase tracking-wider mb-2">
        總資產淨值
      </h2>
      <div className="text-4xl sm:text-5xl font-bold text-[var(--text-primary)] font-mono tracking-tight">
        NT$ {Math.round(grandTotal).toLocaleString('zh-TW')}
      </div>
    </div>
  );
}
