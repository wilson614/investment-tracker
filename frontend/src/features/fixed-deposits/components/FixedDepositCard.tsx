import { formatCurrency } from '../../../utils/currency';
import type { FixedDepositResponse } from '../types';

interface FixedDepositCardProps {
  fixedDeposit: FixedDepositResponse;
  onEdit?: (fixedDeposit: FixedDepositResponse) => void;
  onClose?: (fixedDeposit: FixedDepositResponse) => void;
}

function getStatusBadgeClass(status: FixedDepositResponse['status']) {
  switch (status) {
    case 'Active':
      return 'badge badge-peach';
    case 'Matured':
      return 'badge badge-warning';
    case 'Closed':
      return 'badge badge-success';
    case 'EarlyWithdrawal':
      return 'badge badge-danger';
    default:
      return 'badge badge-cream';
  }
}

function getStatusLabel(status: FixedDepositResponse['status']) {
  switch (status) {
    case 'Active':
      return '進行中';
    case 'Matured':
      return '已到期';
    case 'Closed':
      return '已結清';
    case 'EarlyWithdrawal':
      return '提前解約';
    default:
      return status;
  }
}

function formatDate(date: string) {
  return new Date(date).toLocaleDateString('zh-TW');
}

function getCountdownText(daysRemaining: number, status: FixedDepositResponse['status']) {
  if (status === 'Closed' || status === 'EarlyWithdrawal') {
    return '已結清';
  }

  if (daysRemaining > 0) {
    return `剩餘 ${daysRemaining} 天`;
  }

  if (daysRemaining === 0) {
    return '今日到期';
  }

  return `已逾期 ${Math.abs(daysRemaining)} 天`;
}

function getCountdownClass(daysRemaining: number, status: FixedDepositResponse['status']) {
  if (status === 'Closed' || status === 'EarlyWithdrawal') {
    return 'text-[var(--text-muted)]';
  }

  if (daysRemaining > 30) {
    return 'text-[var(--accent-peach)]';
  }

  if (daysRemaining >= 0) {
    return 'text-[var(--color-warning)]';
  }

  return 'text-[var(--color-danger)]';
}

export function FixedDepositCard({ fixedDeposit, onEdit, onClose }: FixedDepositCardProps) {
  const canClose = fixedDeposit.status === 'Active' || fixedDeposit.status === 'Matured';

  return (
    <div className="card-dark p-5 hover:border-[var(--border-hover)] transition-all group">
      <div className="flex items-start justify-between gap-3 mb-4">
        <div>
          <h3 className="text-xl font-bold text-[var(--accent-cream)]">{fixedDeposit.bankAccountName}</h3>
          <p className="text-sm text-[var(--text-muted)] mt-1">
            起息日：{formatDate(fixedDeposit.startDate)}
          </p>
        </div>
        <span className={getStatusBadgeClass(fixedDeposit.status)}>
          {getStatusLabel(fixedDeposit.status)}
        </span>
      </div>

      <div className="mb-5">
        <p className="text-sm text-[var(--text-muted)] mb-1">本金</p>
        <p className="text-2xl font-bold text-[var(--text-primary)] number-display">
          {formatCurrency(fixedDeposit.principal, fixedDeposit.currency)}{' '}
          <span className="text-sm font-normal text-[var(--text-muted)]">{fixedDeposit.currency}</span>
        </p>
      </div>

      <div className="grid grid-cols-2 gap-4 text-sm mb-4">
        <div>
          <p className="text-[var(--text-muted)] mb-1">預期利息</p>
          <p className="font-medium text-[var(--accent-peach)] number-display">
            {formatCurrency(fixedDeposit.expectedInterest, fixedDeposit.currency)}
          </p>
        </div>
        <div>
          <p className="text-[var(--text-muted)] mb-1">年利率</p>
          <p className="font-medium text-[var(--text-primary)] number-display">
            {fixedDeposit.annualInterestRate.toLocaleString('zh-TW', {
              minimumFractionDigits: 2,
              maximumFractionDigits: 2,
            })}
            %
          </p>
        </div>
        <div>
          <p className="text-[var(--text-muted)] mb-1">到期日</p>
          <p className="font-medium text-[var(--text-primary)]">{formatDate(fixedDeposit.maturityDate)}</p>
        </div>
        <div>
          <p className="text-[var(--text-muted)] mb-1">天期</p>
          <p className="font-medium text-[var(--text-primary)]">{fixedDeposit.termMonths} 個月</p>
        </div>
      </div>

      <div className="pt-4 border-t border-[var(--border-color)] flex items-center justify-between gap-3">
        <p className={`text-sm font-semibold ${getCountdownClass(fixedDeposit.daysRemaining, fixedDeposit.status)}`}>
          {getCountdownText(fixedDeposit.daysRemaining, fixedDeposit.status)}
        </p>

        <div className="flex items-center gap-2">
          {onEdit ? (
            <button
              type="button"
              onClick={() => onEdit(fixedDeposit)}
              className="btn-dark px-3 py-1.5 text-sm"
            >
              編輯
            </button>
          ) : null}
          {onClose && canClose ? (
            <button
              type="button"
              onClick={() => onClose(fixedDeposit)}
              className="btn-accent px-3 py-1.5 text-sm"
            >
              結清
            </button>
          ) : null}
        </div>
      </div>
    </div>
  );
}
