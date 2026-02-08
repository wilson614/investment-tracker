import { Edit, CircleDollarSign, CheckCircle2 } from 'lucide-react';
import { formatCurrency } from '../../../utils/currency';
import type { InstallmentResponse } from '../types';

interface InstallmentListProps {
  installments: InstallmentResponse[];
  processingInstallmentId?: string | null;
  onEdit: (installment: InstallmentResponse) => void;
  onRecordPayment: (installment: InstallmentResponse) => void;
  onPayoff: (installment: InstallmentResponse) => void;
}

function getStatusBadgeClass(status: InstallmentResponse['status']) {
  switch (status) {
    case 'Active':
      return 'badge badge-peach';
    case 'Completed':
      return 'badge badge-success';
    case 'Cancelled':
      return 'badge badge-danger';
    default:
      return 'badge badge-cream';
  }
}

function getStatusLabel(status: InstallmentResponse['status']) {
  switch (status) {
    case 'Active':
      return '進行中';
    case 'Completed':
      return '已完成';
    case 'Cancelled':
      return '已取消';
    default:
      return status;
  }
}

function clampPercentage(value: number): number {
  if (!Number.isFinite(value)) {
    return 0;
  }
  return Math.min(100, Math.max(0, value));
}

function formatDate(dateString: string): string {
  const date = new Date(dateString);
  if (Number.isNaN(date.getTime())) {
    return dateString;
  }
  return date.toLocaleDateString('zh-TW');
}

export function InstallmentList({
  installments,
  processingInstallmentId,
  onEdit,
  onRecordPayment,
  onPayoff,
}: InstallmentListProps) {
  if (installments.length === 0) {
    return (
      <div className="text-center py-12 bg-[var(--bg-secondary)] rounded-xl border border-dashed border-[var(--border-color)]">
        <CircleDollarSign className="w-12 h-12 text-[var(--text-muted)] mx-auto mb-4" />
        <h3 className="text-lg font-medium text-[var(--text-secondary)] mb-2">尚無分期紀錄</h3>
        <p className="text-[var(--text-muted)]">此信用卡尚無分期付款</p>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {installments.map((installment) => {
        const isActive = installment.status === 'Active';
        const isProcessing = processingInstallmentId === installment.id;
        const progress = clampPercentage(installment.progressPercentage);

        return (
          <div
            key={installment.id}
            className="card-dark p-5 border border-[var(--border-color)] hover:border-[var(--border-hover)] transition-all"
          >
            <div className="flex flex-col md:flex-row md:items-start md:justify-between gap-4">
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2 mb-1">
                  <h4 className="text-lg font-semibold text-[var(--text-primary)] break-words">
                    {installment.description}
                  </h4>
                  <span className={getStatusBadgeClass(installment.status)}>{getStatusLabel(installment.status)}</span>
                </div>
                <p className="text-sm text-[var(--text-muted)]">
                  起始日：{formatDate(installment.startDate)}
                  {installment.note ? ` · 備註：${installment.note}` : ''}
                </p>
              </div>

              <div className="flex flex-wrap items-center gap-2">
                {isActive ? (
                  <button
                    type="button"
                    onClick={() => onRecordPayment(installment)}
                    disabled={isProcessing}
                    className="btn-accent px-3 py-1.5 text-sm disabled:opacity-50 disabled:cursor-not-allowed"
                    title="記錄月付"
                  >
                    {isProcessing ? '處理中...' : '記錄月付'}
                  </button>
                ) : null}

                <button
                  type="button"
                  onClick={() => onEdit(installment)}
                  className="btn-dark px-3 py-1.5 text-sm inline-flex items-center gap-1"
                  title="編輯"
                >
                  <Edit size={14} />
                  編輯
                </button>

                {isActive ? (
                  <button
                    type="button"
                    onClick={() => onPayoff(installment)}
                    disabled={isProcessing}
                    className="btn-dark px-3 py-1.5 text-sm inline-flex items-center gap-1 disabled:opacity-50 disabled:cursor-not-allowed"
                    title="提前清償"
                  >
                    <CheckCircle2 size={14} />
                    清償
                  </button>
                ) : null}
              </div>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-4 gap-4 mt-4">
              <div>
                <p className="text-sm text-[var(--text-muted)] mb-1">總金額</p>
                <p className="text-base font-semibold text-[var(--text-primary)] number-display">
                  {formatCurrency(installment.totalAmount, 'TWD')}
                </p>
              </div>
              <div>
                <p className="text-sm text-[var(--text-muted)] mb-1">每月應付</p>
                <p className="text-base font-semibold text-[var(--accent-peach)] number-display">
                  {formatCurrency(installment.monthlyPayment, 'TWD')}
                </p>
              </div>
              <div>
                <p className="text-sm text-[var(--text-muted)] mb-1">剩餘期數</p>
                <p className="text-base font-semibold text-[var(--text-primary)] number-display">
                  {installment.remainingInstallments} / {installment.numberOfInstallments}
                </p>
              </div>
              <div>
                <p className="text-sm text-[var(--text-muted)] mb-1">未繳餘額</p>
                <p className="text-base font-semibold text-[var(--text-primary)] number-display">
                  {formatCurrency(installment.unpaidBalance, 'TWD')}
                </p>
              </div>
            </div>

            <div className="mt-4">
              <div className="flex items-center justify-between mb-1">
                <span className="text-sm text-[var(--text-muted)]">付款進度</span>
                <span className="text-sm text-[var(--text-secondary)] number-display">
                  {progress.toLocaleString('zh-TW', {
                    minimumFractionDigits: 0,
                    maximumFractionDigits: 2,
                  })}
                  %
                </span>
              </div>
              <div className="w-full h-2 rounded-full bg-[var(--bg-tertiary)] overflow-hidden">
                <div
                  className="h-2 rounded-full bg-[var(--accent-peach)] transition-all"
                  style={{ width: `${progress}%` }}
                />
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}
