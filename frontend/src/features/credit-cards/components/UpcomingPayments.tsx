import { CalendarClock } from 'lucide-react';
import { formatCurrency } from '../../../utils/currency';
import type { UpcomingPaymentMonth } from '../types';

interface UpcomingPaymentsProps {
  months: UpcomingPaymentMonth[];
  isLoading?: boolean;
}

function formatMonthLabel(month: string): string {
  const [year, monthValue] = month.split('-');
  if (!year || !monthValue) {
    return month;
  }
  return `${year} 年 ${monthValue} 月`;
}

export function UpcomingPayments({ months, isLoading = false }: UpcomingPaymentsProps) {
  if (isLoading) {
    return (
      <div className="card-dark p-5">
        <p className="text-[var(--text-muted)]">載入未來付款中...</p>
      </div>
    );
  }

  if (months.length === 0) {
    return (
      <div className="card-dark p-5 text-center">
        <CalendarClock className="w-10 h-10 text-[var(--text-muted)] mx-auto mb-3" />
        <p className="text-[var(--text-secondary)]">未來三個月沒有待付分期</p>
      </div>
    );
  }

  return (
    <div className="card-dark p-5">
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
                <span className="ml-1 font-semibold text-[var(--accent-peach)] number-display">
                  {formatCurrency(month.totalAmount, 'TWD')}
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
                  <span className="text-[var(--text-secondary)] number-display whitespace-nowrap">
                    {formatCurrency(payment.amount, 'TWD')}
                  </span>
                </div>
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
