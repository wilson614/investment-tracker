import { Edit2, Trash2 } from 'lucide-react';
import { formatCurrency } from '../../../utils/currency';
import type { FundAllocation } from '../types';

export interface AllocationSummaryItem extends Pick<FundAllocation, 'id' | 'purpose' | 'amount' | 'isDisposable' | 'note'> {}

interface AllocationSummaryProps {
  allocations: AllocationSummaryItem[];
  bankTotal: number;
  unallocatedAmount: number;
  onEdit: (allocation: AllocationSummaryItem) => void;
  onDelete: (id: string) => void;
}

const PURPOSE_DISPLAY_NAMES: Record<FundAllocation['purpose'], string> = {
  EmergencyFund: '緊急預備金',
  FamilyDeposit: '家庭存款',
  General: '一般用途',
  Savings: '儲蓄',
  Investment: '投資準備金',
  Other: '其他',
};

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
      <div className="flex items-start justify-between gap-4">
        <div>
          <h3 className="text-lg font-semibold text-[var(--text-primary)]">資金配置總覽</h3>
          <p className="text-sm text-[var(--text-muted)] mt-1">已配置與未配置金額</p>
        </div>

        <div className="text-right space-y-1">
          <p className="text-xs text-[var(--text-muted)]">已配置</p>
          <p className="text-lg font-semibold text-[var(--text-primary)]">{formatCurrency(totalAllocated, 'TWD')}</p>
          <p className="text-xs text-[var(--text-muted)]">未配置</p>
          <p
            className={`text-sm font-medium ${
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
              <div className="flex items-start justify-between gap-3">
                <div>
                  <div className="flex items-center gap-2">
                    <span className="text-sm text-[var(--text-secondary)]">
                      {PURPOSE_DISPLAY_NAMES[allocation.purpose]}
                    </span>
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
                  {allocation.note ? (
                    <p className="text-xs text-[var(--text-muted)] mt-1 break-words">{allocation.note}</p>
                  ) : null}
                </div>

                <div className="flex items-center gap-1">
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
