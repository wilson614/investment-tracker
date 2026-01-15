import { AlertCircle, Clock } from 'lucide-react';

interface StaleQuoteIndicatorProps {
  isStale: boolean;
  fromCache?: boolean;
  fetchedAt?: string;
  className?: string;
}

/**
 * Indicator component showing when a quote is stale or from cache.
 * Used to warn users that the displayed price may not be current.
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
