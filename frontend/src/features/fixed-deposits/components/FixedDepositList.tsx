import { AlertCircle } from 'lucide-react';
import { FixedDepositCard } from './FixedDepositCard';
import type { FixedDepositResponse } from '../types';

interface FixedDepositListProps {
  fixedDeposits: FixedDepositResponse[];
  onEdit?: (fixedDeposit: FixedDepositResponse) => void;
  onClose?: (fixedDeposit: FixedDepositResponse) => void;
  emptyAction?: React.ReactNode;
}

export function FixedDepositList({
  fixedDeposits,
  onEdit,
  onClose,
  emptyAction,
}: FixedDepositListProps) {
  if (fixedDeposits.length === 0) {
    return (
      <div className="text-center py-12 bg-[var(--bg-secondary)] rounded-xl border border-dashed border-[var(--border-color)]">
        <AlertCircle className="w-12 h-12 text-[var(--text-muted)] mx-auto mb-4" />
        <h3 className="text-lg font-medium text-[var(--text-secondary)] mb-2">尚無定存</h3>
        <p className="text-[var(--text-muted)] mb-6">尚無定存，點擊新增按鈕建立第一筆定存</p>
        {emptyAction}
      </div>
    );
  }

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
      {fixedDeposits.map((fixedDeposit) => (
        <FixedDepositCard
          key={fixedDeposit.id}
          fixedDeposit={fixedDeposit}
          onEdit={onEdit}
          onClose={onClose}
        />
      ))}
    </div>
  );
}
