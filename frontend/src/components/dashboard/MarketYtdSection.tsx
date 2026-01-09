/**
 * MarketYtdSection Component
 * Displays YTD (Year-to-Date) returns for benchmark ETFs
 */

import { Loader2, TrendingUp, TrendingDown, Info } from 'lucide-react';
import { useMarketYtdData } from '../../hooks/useMarketYtdData';
import type { MarketYtdReturn } from '../../types';

interface YtdRowProps {
  item: MarketYtdReturn;
}

function YtdRow({ item }: YtdRowProps) {
  const hasYtd = item.ytdReturnPercent != null;
  const isPositive = hasYtd && item.ytdReturnPercent! >= 0;

  return (
    <div className="flex items-center justify-between py-2.5 border-b border-[var(--border-color)] last:border-b-0">
      <div className="flex flex-col min-w-0">
        <span className="text-[var(--text-primary)] font-medium">{item.marketKey}</span>
        <span className="text-xs text-[var(--text-muted)]">{item.symbol} • {item.name}</span>
      </div>
      <div className="flex items-center gap-3 shrink-0">
        {item.error ? (
          <span className="text-xs text-[var(--color-warning)]" title={item.error}>
            N/A
          </span>
        ) : hasYtd ? (
          <>
            <div className="flex items-center gap-1">
              {isPositive ? (
                <TrendingUp className="w-4 h-4 text-green-400" />
              ) : (
                <TrendingDown className="w-4 h-4 text-red-400" />
              )}
              <span
                className={`text-lg font-bold font-mono ${
                  isPositive ? 'text-green-400' : 'text-red-400'
                }`}
              >
                {isPositive ? '+' : ''}
                {item.ytdReturnPercent!.toFixed(2)}%
              </span>
            </div>
          </>
        ) : (
          <span className="text-xs text-[var(--text-muted)]">缺少年初價格</span>
        )}
      </div>
    </div>
  );
}

interface MarketYtdSectionProps {
  className?: string;
}

export function MarketYtdSection({ className = '' }: MarketYtdSectionProps) {
  const { data, isLoading } = useMarketYtdData();

  return (
    <div className={`card-dark ${className}`}>
      <div className="px-5 py-4 border-b border-[var(--border-color)]">
        <div className="flex items-center gap-2">
          <div>
            <h2 className="text-lg font-bold text-[var(--text-primary)]">年初至今報酬</h2>
            <p className="text-xs text-[var(--text-muted)]">
              {data?.year ? `${data.year} 年度績效 (YTD)` : '基準 ETF 年度績效'}
            </p>
          </div>
          <div className="relative group">
            <Info className="w-4 h-4 text-[var(--text-muted)] cursor-help" />
            <div className="absolute left-0 bottom-full mb-2 hidden group-hover:block z-10">
              <div className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg p-2 shadow-lg text-xs text-[var(--text-secondary)] whitespace-nowrap">
                報酬率 = (現價 - 年初價格) / 年初價格 × 100
              </div>
            </div>
          </div>
        </div>
      </div>

      <div className="p-5">
        {isLoading && !data ? (
          <div className="flex items-center justify-center py-8">
            <Loader2 className="w-6 h-6 animate-spin text-[var(--text-muted)]" />
          </div>
        ) : data && data.benchmarks.length > 0 ? (
          <div>
            {data.benchmarks.map((item) => (
              <YtdRow key={item.marketKey} item={item} />
            ))}
          </div>
        ) : (
          <div className="text-center py-8 text-[var(--text-muted)]">
            無可用資料
          </div>
        )}
      </div>
    </div>
  );
}
