import { CalendarClock } from 'lucide-react';
import { NumberValueSlot, Skeleton } from '../../../components/common';
import { formatCurrency } from '../../../utils/currency';
import type { UpcomingPaymentMonth } from '../types';

interface UpcomingPaymentsProps {
  months: UpcomingPaymentMonth[];
  isLoading?: boolean;
  isValueLoading?: boolean;
}

const UPCOMING_PAYMENTS_SHELL_CLASSNAME = 'card-dark p-5 min-h-[20rem]';

function formatMonthLabel(month: string): string {
  const [year, monthValue] = month.split('-');
  if (!year || !monthValue) {
    return month;
  }
  return `${year} 年 ${monthValue} 月`;
}

function UpcomingPaymentsSkeleton() {
  return (
    <div className={UPCOMING_PAYMENTS_SHELL_CLASSNAME} aria-label="未來付款載入中">
      <div className="flex items-center gap-2 mb-4">
        <CalendarClock className="w-5 h-5 text-[var(--text-muted)]" />
        <Skeleton width="w-40" height="h-6" />
      </div>

      <div className="space-y-4">
        {[1, 2].map((section) => (
          <div
            key={section}
            className="rounded-lg border border-[var(--border-color)] bg-[var(--bg-secondary)] p-4 space-y-3"
          >
            <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-2">
              <Skeleton width="w-32" height="h-5" />
              <Skeleton width="w-28" height="h-4" />
            </div>

            {[1, 2].map((row) => (
              <div key={row} className="flex items-center justify-between gap-3">
                <div className="space-y-1 min-w-0 flex-1">
                  <Skeleton width="w-2/3" height="h-4" />
                  <Skeleton width="w-1/3" height="h-3" />
                </div>
                <Skeleton width="w-16" height="h-4" />
              </div>
            ))}
          </div>
        ))}
      </div>
    </div>
  );
}

export function UpcomingPayments({ months, isLoading = false, isValueLoading = false }: UpcomingPaymentsProps) {
  if (isLoading && months.length === 0) {
    return <UpcomingPaymentsSkeleton />;
  }

  if (months.length === 0) {
    return (
      <div
        className={`${UPCOMING_PAYMENTS_SHELL_CLASSNAME} flex flex-col items-center justify-center text-center`}
      >
        <CalendarClock className="w-10 h-10 text-[var(--text-muted)] mb-3" />
        <p className="text-[var(--text-secondary)]">未來三個月沒有待付分期</p>
      </div>
    );
  }

  return (
    <div className={UPCOMING_PAYMENTS_SHELL_CLASSNAME}>
      <div className="flex items-center gap-2 mb-4">
        <CalendarClock className="w-5 h-5 text-[var(--accent-peach)]" />
        <h3 className="text-lg font-semibold text-[var(--text-primary)]">未來三個月付款預覽</h3>
      </div>

      <div className="space-y-4">
        {months.slice(0, 3).map((month) => (
          <div
            key={month.month}
            className="rounded-lg border border-[var(--border-color)] bg-[var(--bg-secondary)] p-4"
          >
            <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-2 mb-3">
              <h4 className="text-base font-semibold text-[var(--text-primary)]">{formatMonthLabel(month.month)}</h4>
              <p className="text-sm text-[var(--text-secondary)]">
                當月合計：
                <span className="ml-1 align-middle">
                  <NumberValueSlot
                    value={formatCurrency(month.totalAmount, 'TWD')}
                    isLoading={isValueLoading}
                    minWidthClassName="min-w-[10ch]"
                    textClassName="font-semibold text-[var(--accent-peach)] number-display"
                    skeletonHeightClassName="h-4"
                    testId="upcoming-month-total-slot"
                  />
                </span>
              </p>
            </div>

            <div className="space-y-2">
              {month.payments.map((payment) => (
                <div
                  key={`${month.month}-${payment.installmentId}`}
                  className="flex items-center justify-between gap-3 text-sm"
                >
                  <div className="min-w-0">
                    <p className="text-[var(--text-primary)] truncate">{payment.description}</p>
                    <p className="text-[var(--text-muted)] text-xs">{payment.creditCardName}</p>
                  </div>
                  <NumberValueSlot
                    value={formatCurrency(payment.amount, 'TWD')}
                    isLoading={isValueLoading}
                    minWidthClassName="min-w-[9ch]"
                    textClassName="text-[var(--text-secondary)] number-display whitespace-nowrap"
                    skeletonHeightClassName="h-4"
                    testId="upcoming-payment-amount-slot"
                  />
                </div>
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
