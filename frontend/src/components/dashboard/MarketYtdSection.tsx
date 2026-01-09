/**
 * MarketYtdSection Component
 * Displays YTD (Year-to-Date) returns for benchmark ETFs
 */

import { useState, useEffect, useCallback } from 'react';
import { RefreshCw, Loader2, TrendingUp, TrendingDown, AlertCircle } from 'lucide-react';
import { marketDataApi } from '../../services/api';
import type { MarketYtdComparison, MarketYtdReturn } from '../../types';

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
          <span className="text-xs text-[var(--text-muted)]">缺少 Jan 1 價格</span>
        )}
      </div>
    </div>
  );
}

interface MarketYtdSectionProps {
  className?: string;
}

export function MarketYtdSection({ className = '' }: MarketYtdSectionProps) {
  const [data, setData] = useState<MarketYtdComparison | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchData = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const result = await marketDataApi.getYtdComparison();
      setData(result);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to fetch YTD data');
    } finally {
      setIsLoading(false);
    }
  }, []);

  const refresh = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const result = await marketDataApi.refreshYtdComparison();
      setData(result);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to refresh YTD data');
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  if (error) {
    return (
      <div className={`card-dark ${className}`}>
        <div className="px-5 py-4 border-b border-[var(--border-color)] flex items-center justify-between">
          <div>
            <h2 className="text-lg font-bold text-[var(--text-primary)]">市場 YTD 表現</h2>
            <p className="text-xs text-[var(--text-muted)]">基準 ETF 年初至今報酬</p>
          </div>
          <button
            onClick={fetchData}
            className="btn-dark p-2"
            title="重新整理"
          >
            <RefreshCw className="w-4 h-4" />
          </button>
        </div>
        <div className="p-5">
          <div className="flex items-center gap-3 text-[var(--text-muted)]">
            <AlertCircle className="w-5 h-5 text-yellow-500" />
            <div>
              <p className="text-sm">暫時無法取得 YTD 資料</p>
              <p className="text-xs mt-1">{error}</p>
            </div>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className={`card-dark ${className}`}>
      <div className="px-5 py-4 border-b border-[var(--border-color)] flex items-center justify-between">
        <div>
          <h2 className="text-lg font-bold text-[var(--text-primary)]">市場 YTD 表現</h2>
          <p className="text-xs text-[var(--text-muted)]">
            {data?.year ? `${data.year} 年初至今` : '基準 ETF 年初至今報酬'}
          </p>
        </div>
        <button
          onClick={refresh}
          disabled={isLoading}
          className="btn-dark p-2 disabled:opacity-50"
          title="重新整理"
        >
          {isLoading ? (
            <Loader2 className="w-4 h-4 animate-spin" />
          ) : (
            <RefreshCw className="w-4 h-4" />
          )}
        </button>
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
            <p className="text-xs text-[var(--text-muted)] mt-4">
              公式: (現價 - Jan 1 價格) / Jan 1 價格 × 100
            </p>
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
