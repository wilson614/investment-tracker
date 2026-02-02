import { AlertTriangle } from 'lucide-react';

/**
 * XirrWarningBadge
 *
 * 當 XIRR 計算期間過短（< 3 個月）時顯示警告標誌。
 * 短期計算的 XIRR 年化報酬率可能會有較大誤差。
 */
interface XirrWarningBadgeProps {
  /** 最早的交易日期 (ISO string) */
  earliestTransactionDate: string | null;
  /** 計算基準日期 (ISO string) */
  asOfDate: string;
  /** 額外 className */
  className?: string;
}

/** 最小建議計算期間（月） */
const MIN_MONTHS_THRESHOLD = 3;

/**
 * 計算兩個日期之間的月份差異
 */
function getMonthsDifference(startDate: Date, endDate: Date): number {
  const years = endDate.getFullYear() - startDate.getFullYear();
  const months = endDate.getMonth() - startDate.getMonth();
  const days = endDate.getDate() - startDate.getDate();

  let totalMonths = years * 12 + months;
  // 如果結束日期的日期比開始日期小，減少一個月
  if (days < 0) {
    totalMonths -= 1;
  }

  return totalMonths;
}

/**
 * 判斷 XIRR 計算期間是否過短
 */
export function isXirrPeriodTooShort(
  earliestTransactionDate: string | null,
  asOfDate: string
): boolean {
  if (!earliestTransactionDate) {
    return false;
  }

  const startDate = new Date(earliestTransactionDate);
  const endDate = new Date(asOfDate);

  if (isNaN(startDate.getTime()) || isNaN(endDate.getTime())) {
    return false;
  }

  const monthsDiff = getMonthsDifference(startDate, endDate);
  return monthsDiff < MIN_MONTHS_THRESHOLD;
}

/**
 * 顯示 XIRR 計算期間過短的警告標誌
 */
export function XirrWarningBadge({
  earliestTransactionDate,
  asOfDate,
  className = '',
}: XirrWarningBadgeProps) {
  if (!isXirrPeriodTooShort(earliestTransactionDate, asOfDate)) {
    return null;
  }

  return (
    <span
      className={`inline-flex items-center gap-1 text-[var(--color-warning)] ${className}`}
      title="計算期間小於 3 個月，XIRR 年化報酬率可能誤差較大"
    >
      <AlertTriangle className="w-3.5 h-3.5" />
    </span>
  );
}
