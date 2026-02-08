import { useState } from 'react';
import { Plus, CreditCard as CreditCardIcon } from 'lucide-react';
import { LoadingSpinner, ErrorDisplay } from '../components/common';
import { ConfirmationModal } from '../components/modals/ConfirmationModal';
import { CreditCardForm } from '../features/credit-cards/components/CreditCardForm';
import { CreditCardList } from '../features/credit-cards/components/CreditCardList';
import { useCreditCards } from '../features/credit-cards/hooks/useCreditCards';
import type {
  CreditCardResponse,
  CreateCreditCardRequest,
  UpdateCreditCardRequest,
} from '../features/credit-cards/types';

export function CreditCardsPage() {
  const {
    creditCards,
    isLoading,
    error,
    refetch,
    createCreditCard,
    updateCreditCard,
    deactivateCreditCard,
  } = useCreditCards();

  const [showForm, setShowForm] = useState(false);
  const [editingCard, setEditingCard] = useState<CreditCardResponse | undefined>(undefined);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [showDeactivateModal, setShowDeactivateModal] = useState(false);
  const [deactivatingCardId, setDeactivatingCardId] = useState<string | null>(null);

  const activeCards = creditCards.filter((card) => card.isActive);

  const handleCreate = () => {
    setEditingCard(undefined);
    setShowForm(true);
  };

  const handleEdit = (card: CreditCardResponse) => {
    setEditingCard(card);
    setShowForm(true);
  };

  const handleDeactivateClick = (id: string) => {
    setDeactivatingCardId(id);
    setShowDeactivateModal(true);
  };

  const handleConfirmDeactivate = async () => {
    if (!deactivatingCardId) return;
    await deactivateCreditCard(deactivatingCardId);
    setDeactivatingCardId(null);
  };

  const handleSubmit = async (data: CreateCreditCardRequest | UpdateCreditCardRequest) => {
    setIsSubmitting(true);
    try {
      if (editingCard) {
        await updateCreditCard(editingCard.id, data as UpdateCreditCardRequest);
      } else {
        await createCreditCard(data as CreateCreditCardRequest);
      }
      setShowForm(false);
      return true;
    } catch {
      return false;
    } finally {
      setIsSubmitting(false);
    }
  };

  if (isLoading && creditCards.length === 0) {
    return <LoadingSpinner />;
  }

  if (error) {
    return <ErrorDisplay message={error} onRetry={refetch} />;
  }

  return (
    <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-8">
      <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
        <div>
          <h1 className="text-2xl font-bold text-[var(--text-primary)] mb-2">信用卡</h1>
          <p className="text-[var(--text-secondary)]">管理信用卡與追蹤分期未繳餘額</p>
        </div>
        <button
          type="button"
          onClick={handleCreate}
          className="btn-accent flex items-center gap-2"
        >
          <Plus className="w-5 h-5" />
          新增信用卡
        </button>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        <div className="metric-card metric-card-peach">
          <div className="flex items-center gap-3 mb-2">
            <div className="p-2 bg-[var(--accent-peach)] rounded-lg text-[var(--bg-primary)]">
              <CreditCardIcon className="w-5 h-5" />
            </div>
            <span className="text-sm font-medium text-[var(--accent-peach)] uppercase tracking-wider">
              啟用信用卡
            </span>
          </div>
          <div className="text-3xl font-bold text-[var(--text-primary)] mt-4">
            {activeCards.length.toLocaleString('zh-TW')} 張
          </div>
        </div>

        <div className="metric-card metric-card-butter">
          <div className="flex items-center gap-3 mb-2">
            <div className="p-2 bg-[var(--accent-butter)] rounded-lg text-[var(--bg-primary)]">
              <CreditCardIcon className="w-5 h-5" />
            </div>
            <span className="text-sm font-medium text-[var(--accent-butter)] uppercase tracking-wider">
              全部信用卡
            </span>
          </div>
          <div className="text-3xl font-bold text-[var(--text-primary)] mt-4">
            {creditCards.length.toLocaleString('zh-TW')} 張
          </div>
        </div>
      </div>

      <CreditCardList
        creditCards={activeCards}
        onEdit={handleEdit}
        onDeactivate={handleDeactivateClick}
      />

      {showForm && (
        <CreditCardForm
          initialData={editingCard}
          onSubmit={handleSubmit}
          onCancel={() => setShowForm(false)}
          isLoading={isSubmitting}
        />
      )}

      <ConfirmationModal
        isOpen={showDeactivateModal}
        onClose={() => setShowDeactivateModal(false)}
        onConfirm={handleConfirmDeactivate}
        title="停用信用卡"
        message="確定要停用此信用卡嗎？停用後將無法再新增該卡分期。"
        confirmText="停用"
        isDestructive={true}
      />
    </div>
  );
}
