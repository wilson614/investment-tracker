import { TrendingUp, Landmark } from 'lucide-react';
import { Skeleton } from '../../../components/common/SkeletonLoader';
import type { TotalAssetsSummary } from '../types';

interface AssetCategorySummaryProps {
  data?: TotalAssetsSummary;
  isLoading: boolean;
}

export function AssetCategorySummary({ data, isLoading }: AssetCategorySummaryProps) {
  if (isLoading) {
    return (
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {[1, 2].map((i) => (
          <div key={i} className="card-dark p-6 space-y-3">
            <div className="flex justify-between items-center">
              <Skeleton width="w-24" height="h-5" />
              <Skeleton width="w-10" height="h-10" circle />
            </div>
            <Skeleton width="w-32" height="h-8" />
            <Skeleton width="w-16" height="h-4" />
          </div>
        ))}
      </div>
    );
  }

  const items = [
    {
      title: '投資部位',
      amount: data?.investmentTotal ?? 0,
      percentage: data?.investmentPercentage ?? 0,
      icon: <TrendingUp className="w-5 h-5 text-[var(--accent-peach)]" />,
      colorClass: 'text-[var(--accent-peach)]',
      bgClass: 'bg-[var(--accent-peach-soft)]',
      barClass: 'bg-[var(--accent-peach)]',
      description: '股票與外幣資產',
    },
    {
      title: '銀行存款',
      amount: data?.bankTotal ?? 0,
      percentage: data?.bankPercentage ?? 0,
      icon: <Landmark className="w-5 h-5 text-blue-400" />,
      colorClass: 'text-blue-400',
      bgClass: 'bg-blue-400/10',
      barClass: 'bg-blue-400',
      description: '台幣活存與定存',
    },
  ];

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
      {items.map((item) => (
        <div key={item.title} className="card-dark p-6 transition-transform hover:scale-[1.01]">
          <div className="flex justify-between items-start mb-4">
            <div>
              <h3 className="text-[var(--text-secondary)] font-medium mb-1">{item.title}</h3>
              <p className="text-xs text-[var(--text-muted)]">{item.description}</p>
            </div>
            <div className={`p-3 rounded-full ${item.bgClass}`}>
              {item.icon}
            </div>
          </div>

          <div className="space-y-1">
            <div className="text-2xl font-bold font-mono text-[var(--text-primary)]">
              NT$ {Math.round(item.amount).toLocaleString('zh-TW')}
            </div>
            <div className="flex items-center gap-2">
              <div className="flex-1 h-1.5 bg-[var(--bg-tertiary)] rounded-full overflow-hidden">
                <div
                  className={`h-full rounded-full ${item.barClass}`}
                  style={{ width: `${item.percentage}%` }}
                />
              </div>
              <span className={`text-sm font-medium ${item.colorClass}`}>
                {item.percentage.toFixed(1)}%
              </span>
            </div>
          </div>
        </div>
      ))}
    </div>
  );
}
