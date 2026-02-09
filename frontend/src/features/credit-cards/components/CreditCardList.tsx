import { Edit, CreditCard } from 'lucide-react';
import { formatCurrency } from '../../../utils/currency';
import type { CreditCardResponse } from '../types';

interface CreditCardListProps {
  creditCards: CreditCardResponse[];
  selectedCardId?: string | null;
  onSelect?: (card: CreditCardResponse) => void;
  onEdit: (card: CreditCardResponse) => void;
}

export function CreditCardList({
  creditCards,
  selectedCardId,
  onSelect,
  onEdit,
}: CreditCardListProps) {
  if (creditCards.length === 0) {
    return (
      <div className="text-center py-12 bg-[var(--bg-secondary)] rounded-xl border border-dashed border-[var(--border-color)]">
        <CreditCard className="w-12 h-12 text-[var(--text-muted)] mx-auto mb-4" />
        <h3 className="text-lg font-medium text-[var(--text-secondary)] mb-2">尚無信用卡</h3>
        <p className="text-[var(--text-muted)]">尚無信用卡，點擊新增按鈕建立第一張信用卡</p>
      </div>
    );
  }

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
      {creditCards.map((card) => {
        const isSelected = selectedCardId === card.id;

        return (
          <div
            key={card.id}
            className={`card-dark p-5 hover:border-[var(--border-hover)] transition-all group relative ${
              onSelect ? 'cursor-pointer' : ''
            } ${isSelected ? 'ring-2 ring-[var(--accent-peach)] border-[var(--accent-peach)]' : ''}`}
            onClick={() => onSelect?.(card)}
          >
            <div className="absolute bottom-4 right-4 flex gap-2 opacity-0 group-hover:opacity-100 transition-opacity">
              <button
                type="button"
                onClick={(e) => {
                  e.stopPropagation();
                  onEdit(card);
                }}
                className="p-1.5 text-[var(--text-muted)] hover:text-[var(--accent-peach)] hover:bg-[var(--bg-tertiary)] rounded transition-colors"
                title="編輯"
              >
                <Edit size={16} />
              </button>
            </div>

            <div className="mb-4">
              <h3 className="text-xl font-bold text-[var(--accent-cream)] truncate mb-1">{card.cardName}</h3>
              <p className="text-sm text-[var(--text-secondary)]">{card.bankName}</p>
              <p className="text-xs text-[var(--text-muted)] mt-1">結帳日：每月 {card.billingCycleDay} 日</p>
              {card.note ? (
                <p className="text-sm text-[var(--text-muted)] mt-2 line-clamp-2">{card.note}</p>
              ) : null}
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div>
                <p className="text-sm text-[var(--text-muted)] mb-1">進行中分期</p>
                <p className="text-lg font-semibold text-[var(--accent-peach)] number-display">
                  {card.activeInstallmentsCount.toLocaleString('zh-TW')} 筆
                </p>
              </div>
              <div>
                <p className="text-sm text-[var(--text-muted)] mb-1">未繳總額</p>
                <p className="text-lg font-semibold text-[var(--text-primary)] number-display">
                  {formatCurrency(card.totalUnpaidBalance, 'TWD')}
                </p>
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}
