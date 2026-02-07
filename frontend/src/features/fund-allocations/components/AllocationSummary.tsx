import { Edit2, Trash2 } from 'lucide-react';
import { formatCurrency } from '../../../utils/currency';
import type { FundAllocation } from '../types';

export type AllocationSummaryItem = Pick<FundAllocation, 'id' | 'purpose' | 'amount' | 'isDisposable'>;

interface AllocationSummaryProps {
  allocations: AllocationSummaryItem[];
  bankTotal: number;
  unallocatedAmount: number;
  onEdit: (allocation: AllocationSummaryItem) => void;
  onDelete: (id: string) => void;
}

const PURPOSE_DISPLAY_NAMES: Record<string, string> = {
  EmergencyFund: '緊急預備金',
  FamilyDeposit: '家庭存款',
  General: '一般用途',
  Savings: '儲蓄',
  Investment: '投資準備金',
  Other: '其他',
};

function getPurposeLabel(purpose: string): string {
  return PURPOSE_DISPLAY_NAMES[purpose] ?? purpose;
}

export function AllocationSummary({
  allocations,
  bankTotal,
  unallocatedAmount,
  onEdit,
  onDelete,
}: AllocationSummaryProps) {
  const totalAllocated = allocations.reduce((sum, item) => sum + item.amount, 0);
  const normalizedUnallocated = Number.isFinite(unallocatedAmount)
    ? unallocatedAmount
    : bankTotal - totalAllocated;

  return (
    <div className="card-dark p-6 space-y-5">
      <div className="grid grid-cols-2 gap-4 mb-5">
        <div className="rounded-lg bg-[var(--bg-tertiary)]/50 p-4 text-center">
          <p className="text-xs text-[var(--text-muted)] mb-1">特殊配置</p>
          <p className="text-xl font-bold text-[var(--text-primary)]">{formatCurrency(totalAllocated, 'TWD')}</p>
        </div>
        <div className="rounded-lg bg-[var(--bg-tertiary)]/50 p-4 text-center">
          <p className="text-xs text-[var(--text-muted)] mb-1">一般存款</p>
          <p
            className={`text-xl font-bold ${
              normalizedUnallocated < 0 ? 'text-[var(--color-danger)]' : 'text-[var(--accent-peach)]'
            }`}
          >
            {formatCurrency(normalizedUnallocated, 'TWD')}
          </p>
        </div>
      </div>

      <div className="space-y-2">
        {allocations.length === 0 ? (
          <div className="text-sm text-[var(--text-muted)] border border-dashed border-[var(--border-color)] rounded-lg p-4 text-center">
            尚未建立任何資金配置
          </div>
        ) : (
          allocations.map((allocation) => (
            <div
              key={allocation.id}
              className="border border-[var(--border-color)] rounded-lg p-3 bg-[var(--bg-tertiary)]/50"
            >
              <div className="flex items-center justify-between gap-3">
                <div className="min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="text-sm text-[var(--text-secondary)] truncate">{getPurposeLabel(allocation.purpose)}</span>
                    <span
                      className={`text-xs px-1.5 py-0.5 rounded ${
                        allocation.isDisposable
                          ? 'bg-[var(--accent-peach)]/20 text-[var(--accent-peach)]'
                          : 'bg-[var(--text-muted)]/20 text-[var(--text-muted)]'
                      }`}
                    >
                      {allocation.isDisposable ? '可動用' : '不可動用'}
                    </span>
                  </div>
                  <div className="text-base font-semibold text-[var(--text-primary)] mt-1">
                    {formatCurrency(allocation.amount, 'TWD')}
                  </div>
                </div>

                <div className="flex items-center gap-1 shrink-0">
                  <button
                    type="button"
                    onClick={() => onEdit(allocation)}
                    className="p-1.5 text-[var(--text-muted)] hover:text-[var(--accent-peach)] hover:bg-[var(--bg-secondary)] rounded transition-colors"
                    title="編輯配置"
                  >
                    <Edit2 size={16} />
                  </button>
                  <button
                    type="button"
                    onClick={() => onDelete(allocation.id)}
                    className="p-1.5 text-[var(--text-muted)] hover:text-[var(--color-danger)] hover:bg-[var(--bg-secondary)] rounded transition-colors"
                    title="刪除配置"
                  >
                    <Trash2 size={16} />
                  </button>
                </div>
              </div>
            </div>
          ))
        )}
      </div>
    </div>
  );
}
