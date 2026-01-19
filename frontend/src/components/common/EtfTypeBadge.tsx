/**
 * EtfTypeBadge
 *
 * ETF 類型標籤：用於顯示累積型/配息型/未分類，並提示是否為人工確認。
 */
import { HelpCircle, Check, TrendingUp } from 'lucide-react';

export type EtfType = 'Unknown' | 'Accumulating' | 'Distributing';

interface EtfTypeBadgeProps {
  /** ETF 類型 */
  type: EtfType;
  /** 是否為人工確認（false 表示自動判斷） */
  isConfirmed: boolean;
  /** 額外 className */
  className?: string;
}

const TYPE_CONFIG: Record<EtfType, { label: string; icon: React.ReactNode; color: string }> = {
  Unknown: {
    label: '未分類',
    icon: <HelpCircle className="w-3 h-3" />,
    color: 'text-[var(--text-muted)] bg-[var(--bg-tertiary)]',
  },
  Accumulating: {
    label: '累積型',
    icon: <TrendingUp className="w-3 h-3" />,
    color: 'text-[var(--color-success)] bg-[var(--color-success)]/10',
  },
  Distributing: {
    label: '配息型',
    icon: <Check className="w-3 h-3" />,
    color: 'text-[var(--accent-butter)] bg-[var(--accent-butter)]/10',
  },
};

export function EtfTypeBadge({ type, isConfirmed, className = '' }: EtfTypeBadgeProps) {
  const config = TYPE_CONFIG[type];

  return (
    <span
      className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs ${config.color} ${className}`}
      title={isConfirmed ? '已確認' : '自動判斷（可能不準確）'}
    >
      {config.icon}
      <span>{config.label}</span>
      {!isConfirmed && type !== 'Unknown' && (
        <span className="opacity-50">?</span>
      )}
    </span>
  );
}
