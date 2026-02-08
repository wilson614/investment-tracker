import { useEffect, useMemo, useState } from 'react';
import { Plus, CreditCard as CreditCardIcon } from 'lucide-react';
import { ErrorDisplay, Skeleton } from '../components/common';
import { ConfirmationModal } from '../components/modals/ConfirmationModal';
import { CreditCardForm } from '../features/credit-cards/components/CreditCardForm';
import { CreditCardList } from '../features/credit-cards/components/CreditCardList';
import { InstallmentForm } from '../features/credit-cards/components/InstallmentForm';
import { InstallmentList } from '../features/credit-cards/components/InstallmentList';
import { UpcomingPayments } from '../features/credit-cards/components/UpcomingPayments';
import { useCreditCards } from '../features/credit-cards/hooks/useCreditCards';
import { useInstallments } from '../features/credit-cards/hooks/useInstallments';
import type {
  CreditCardResponse,
  CreateCreditCardRequest,
  UpdateCreditCardRequest,
  CreateInstallmentRequest,
  UpdateInstallmentRequest,
  InstallmentResponse,
} from '../features/credit-cards/types';

function CreditCardsPageSkeleton() {
  return (
    <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-8" aria-label="信用卡頁面載入中">
      <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
        <div className="space-y-2">
          <Skeleton width="w-28" height="h-8" />
          <Skeleton width="w-64" height="h-5" />
        </div>
        <Skeleton width="w-32" height="h-10" className="rounded-lg" />
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        {[1, 2].map((item) => (
          <div key={item} className="metric-card p-6 space-y-3">
            <div className="flex items-center gap-3">
              <Skeleton width="w-9" height="h-9" className="rounded-lg" />
              <Skeleton width="w-24" height="h-4" />
            </div>
            <Skeleton width="w-20" height="h-9" />
          </div>
        ))}
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {[1, 2, 3].map((item) => (
          <div key={item} className="card-dark p-5 space-y-4 border border-[var(--border-color)]">
            <div className="space-y-2">
              <Skeleton width="w-32" height="h-6" />
              <Skeleton width="w-24" height="h-4" />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div>
                <Skeleton width="w-16" height="h-4" />
                <Skeleton width="w-14" height="h-6" className="mt-2" />
              </div>
              <div>
                <Skeleton width="w-16" height="h-4" />
                <Skeleton width="w-20" height="h-6" className="mt-2" />
              </div>
            </div>
          </div>
        ))}
      </div>

      <div className="card-dark p-5 space-y-4">
        <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-3">
          <div className="space-y-2">
            <Skeleton width="w-48" height="h-7" />
            <Skeleton width="w-72" height="h-4" />
          </div>
          <Skeleton width="w-24" height="h-9" className="rounded-lg" />
        </div>

        <InstallmentListSkeleton />
      </div>
    </div>
  );
}

function InstallmentListSkeleton() {
  return (
    <div className="space-y-4" aria-label="分期清單載入中">
      {[1, 2].map((item) => (
        <div key={item} className="card-dark p-5 border border-[var(--border-color)] space-y-4">
          <div className="flex flex-col md:flex-row md:items-start md:justify-between gap-4">
            <div className="space-y-2 min-w-0">
              <Skeleton width="w-40" height="h-6" />
              <Skeleton width="w-60" height="h-4" />
            </div>
            <div className="flex gap-2">
              <Skeleton width="w-20" height="h-8" className="rounded" />
              <Skeleton width="w-16" height="h-8" className="rounded" />
            </div>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
            {[1, 2, 3, 4].map((metric) => (
              <div key={metric} className="space-y-2">
                <Skeleton width="w-16" height="h-4" />
                <Skeleton width="w-20" height="h-5" />
              </div>
            ))}
          </div>

          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <Skeleton width="w-20" height="h-4" />
              <Skeleton width="w-12" height="h-4" />
            </div>
            <Skeleton width="w-full" height="h-2" className="rounded-full" />
          </div>
        </div>
      ))}
    </div>
  );
}

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

  const [showCardForm, setShowCardForm] = useState(false);
  const [editingCard, setEditingCard] = useState<CreditCardResponse | undefined>(undefined);
  const [isCardSubmitting, setIsCardSubmitting] = useState(false);
  const [showDeactivateModal, setShowDeactivateModal] = useState(false);
  const [deactivatingCardId, setDeactivatingCardId] = useState<string | null>(null);
  const [selectedCardId, setSelectedCardId] = useState<string | null>(null);

  const [showInstallmentForm, setShowInstallmentForm] = useState(false);
  const [editingInstallment, setEditingInstallment] = useState<InstallmentResponse | undefined>(undefined);
  const [isInstallmentSubmitting, setIsInstallmentSubmitting] = useState(false);
  const [processingInstallmentId, setProcessingInstallmentId] = useState<string | null>(null);
  const [payoffInstallmentTarget, setPayoffInstallmentTarget] = useState<InstallmentResponse | null>(null);

  const activeCards = useMemo(() => creditCards.filter((card) => card.isActive), [creditCards]);

  useEffect(() => {
    if (activeCards.length === 0) {
      setSelectedCardId(null);
      return;
    }

    setSelectedCardId((previous) => {
      if (previous && activeCards.some((card) => card.id === previous)) {
        return previous;
      }
      return activeCards[0].id;
    });
  }, [activeCards]);

  const selectedCard = useMemo(
    () => activeCards.find((card) => card.id === selectedCardId) ?? null,
    [activeCards, selectedCardId]
  );

  const {
    installments,
    upcomingPayments,
    isLoading: isInstallmentsLoading,
    isUpcomingLoading,
    error: installmentsError,
    upcomingError,
    refetch: refetchInstallments,
    refetchUpcoming,
    createInstallment,
    updateInstallment,
    recordPayment,
    payoffInstallment,
  } = useInstallments({
    creditCardId: selectedCard?.id,
    upcomingMonths: 3,
  });

  const handleCreateCard = () => {
    setEditingCard(undefined);
    setShowCardForm(true);
  };

  const handleEditCard = (card: CreditCardResponse) => {
    setEditingCard(card);
    setShowCardForm(true);
  };

  const handleDeactivateClick = (id: string) => {
    setDeactivatingCardId(id);
    setShowDeactivateModal(true);
  };

  const handleConfirmDeactivate = async () => {
    if (!deactivatingCardId) {
      return;
    }

    await deactivateCreditCard(deactivatingCardId);
    setDeactivatingCardId(null);
  };

  const handleCardSubmit = async (data: CreateCreditCardRequest | UpdateCreditCardRequest) => {
    setIsCardSubmitting(true);
    try {
      if (editingCard) {
        await updateCreditCard(editingCard.id, data as UpdateCreditCardRequest);
      } else {
        await createCreditCard(data as CreateCreditCardRequest);
      }
      setShowCardForm(false);
      return true;
    } catch {
      return false;
    } finally {
      setIsCardSubmitting(false);
    }
  };

  const handleCreateInstallment = () => {
    if (!selectedCard) {
      return;
    }

    setEditingInstallment(undefined);
    setShowInstallmentForm(true);
  };

  const handleEditInstallment = (installment: InstallmentResponse) => {
    setEditingInstallment(installment);
    setShowInstallmentForm(true);
  };

  const handleInstallmentSubmit = async (data: CreateInstallmentRequest | UpdateInstallmentRequest) => {
    setIsInstallmentSubmitting(true);
    try {
      if (editingInstallment) {
        await updateInstallment(editingInstallment.id, data as UpdateInstallmentRequest);
      } else {
        await createInstallment(data as CreateInstallmentRequest);
      }

      setShowInstallmentForm(false);
      setEditingInstallment(undefined);
      return true;
    } catch {
      return false;
    } finally {
      setIsInstallmentSubmitting(false);
    }
  };

  const handleRecordPayment = async (installment: InstallmentResponse) => {
    setProcessingInstallmentId(installment.id);
    try {
      await recordPayment(installment.id);
    } finally {
      setProcessingInstallmentId(null);
    }
  };

  const handlePayoffClick = (installment: InstallmentResponse) => {
    setPayoffInstallmentTarget(installment);
  };

  const handleConfirmPayoff = async () => {
    const targetId = payoffInstallmentTarget?.id;
    if (!targetId) {
      return;
    }

    setProcessingInstallmentId(targetId);
    try {
      await payoffInstallment(targetId);
      setPayoffInstallmentTarget(null);
    } finally {
      setProcessingInstallmentId(null);
    }
  };

  if (isLoading && creditCards.length === 0) {
    return <CreditCardsPageSkeleton />;
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
          onClick={handleCreateCard}
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
        selectedCardId={selectedCard?.id ?? null}
        onSelect={(card) => setSelectedCardId(card.id)}
        onEdit={handleEditCard}
        onDeactivate={handleDeactivateClick}
      />

      {selectedCard ? (
        <div className="space-y-6">
          <div className="card-dark p-5 space-y-4">
            <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-3">
              <div>
                <h2 className="text-xl font-bold text-[var(--text-primary)]">{selectedCard.cardName} 分期清單</h2>
                <p className="text-sm text-[var(--text-muted)]">
                  {selectedCard.bankName} · 可單擊「記錄月付」快速扣減一期（SC-005）
                </p>
              </div>
              <button
                type="button"
                onClick={handleCreateInstallment}
                className="btn-accent inline-flex items-center gap-2"
              >
                <Plus className="w-4 h-4" />
                新增分期
              </button>
            </div>

            {isInstallmentsLoading && installments.length === 0 ? (
              <InstallmentListSkeleton />
            ) : installmentsError ? (
              <ErrorDisplay message={installmentsError} onRetry={() => void refetchInstallments()} />
            ) : (
              <InstallmentList
                installments={installments}
                processingInstallmentId={processingInstallmentId}
                onEdit={handleEditInstallment}
                onRecordPayment={handleRecordPayment}
                onPayoff={handlePayoffClick}
              />
            )}
          </div>

          {upcomingError ? (
            <ErrorDisplay message={upcomingError} onRetry={() => void refetchUpcoming()} />
          ) : (
            <UpcomingPayments months={upcomingPayments} isLoading={isUpcomingLoading} />
          )}
        </div>
      ) : null}

      {showCardForm && (
        <CreditCardForm
          initialData={editingCard}
          onSubmit={handleCardSubmit}
          onCancel={() => setShowCardForm(false)}
          isLoading={isCardSubmitting}
        />
      )}

      {showInstallmentForm && selectedCard && (
        <InstallmentForm
          creditCardId={selectedCard.id}
          initialData={editingInstallment}
          onSubmit={handleInstallmentSubmit}
          onCancel={() => {
            setShowInstallmentForm(false);
            setEditingInstallment(undefined);
          }}
          isLoading={isInstallmentSubmitting}
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

      <ConfirmationModal
        isOpen={Boolean(payoffInstallmentTarget)}
        onClose={() => setPayoffInstallmentTarget(null)}
        onConfirm={handleConfirmPayoff}
        title="提前清償分期"
        message={
          payoffInstallmentTarget ? (
            <span>
              確定要將「{payoffInstallmentTarget.description}」標記為提前清償嗎？
              <br />
              清償後將不再計入未來待付金額。
            </span>
          ) : (
            ''
          )
        }
        confirmText="確認清償"
      />
    </div>
  );
}
