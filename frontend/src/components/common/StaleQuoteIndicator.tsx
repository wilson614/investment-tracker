import { AlertCircle, Clock } from 'lucide-react';

/**
 * StaleQuoteIndicator
 *
 * 報價狀態提示：顯示「過期報價」或「快取」標記，提醒使用者目前價格可能非即時。
 */
interface StaleQuoteIndicatorProps {
  /** 是否判定為過期報價 */
  isStale: boolean;
  /** 是否來自快取（即使未過期也可能想顯示） */
  fromCache?: boolean;
  /** 報價取得時間 (ISO string) */
  fetchedAt?: string;
  /** 額外 className */
  className?: string;
}

/**
 * 指示目前顯示的報價是否為快取或已過期。
 */
export function StaleQuoteIndicator({
  isStale,
  fromCache,
  fetchedAt,
  className = '',
}: StaleQuoteIndicatorProps) {
  if (!isStale && !fromCache) {
    return null;
  }

  const formatTime = (dateStr: string) => {
    try {
      const date = new Date(dateStr);
      return date.toLocaleString('zh-TW', {
        month: 'numeric',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
      });
    } catch {
      return dateStr;
    }
  };

  if (isStale) {
    return (
      <div className={`inline-flex items-center gap-1 text-[var(--color-warning)] ${className}`}>
        <AlertCircle className="w-3 h-3" />
        <span className="text-xs">過期報價</span>
        {fetchedAt && (
          <span className="text-xs text-[var(--text-muted)]">({formatTime(fetchedAt)})</span>
        )}
      </div>
    );
  }

  if (fromCache) {
    return (
      <div className={`inline-flex items-center gap-1 text-[var(--text-muted)] ${className}`}>
        <Clock className="w-3 h-3" />
        <span className="text-xs">快取</span>
        {fetchedAt && (
          <span className="text-xs">({formatTime(fetchedAt)})</span>
        )}
      </div>
    );
  }

  return null;
}
