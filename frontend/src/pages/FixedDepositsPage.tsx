import { useMemo, useState } from 'react';
import { Landmark, Plus } from 'lucide-react';
import { ErrorDisplay, Skeleton } from '../components/common';
import { ConfirmationModal } from '../components/modals/ConfirmationModal';
import { useBankAccounts } from '../features/bank-accounts/hooks/useBankAccounts';
import { formatCurrency } from '../utils/currency';
import { FixedDepositForm } from '../features/fixed-deposits/components/FixedDepositForm';
import { FixedDepositList } from '../features/fixed-deposits/components/FixedDepositList';
import { useFixedDeposits } from '../features/fixed-deposits/hooks/useFixedDeposits';
import type {
  CloseFixedDepositRequest,
  CreateFixedDepositRequest,
  FixedDepositResponse,
} from '../features/fixed-deposits/types';

function FixedDepositsPageSkeleton() {
  return (
    <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-8" aria-label="定存頁面載入中">
      <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
        <div className="space-y-2">
          <Skeleton width="w-20" height="h-8" />
          <Skeleton width="w-64" height="h-5" />
        </div>
        <Skeleton width="w-28" height="h-10" className="rounded-lg" />
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        {[1, 2].map((item) => (
          <div key={item} className="metric-card p-6 space-y-3">
            <div className="flex items-center gap-3">
              <Skeleton width="w-9" height="h-9" className="rounded-lg" />
              <Skeleton width="w-36" height="h-4" />
            </div>
            <Skeleton width="w-44" height="h-9" />
          </div>
        ))}
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {[1, 2, 3].map((item) => (
          <div key={item} className="card-dark p-5 border border-[var(--border-color)] space-y-4">
            <div className="space-y-2">
              <Skeleton width="w-28" height="h-6" />
              <Skeleton width="w-36" height="h-4" />
            </div>
            <div className="grid grid-cols-2 gap-4">
              {[1, 2].map((metric) => (
                <div key={metric} className="space-y-2">
                  <Skeleton width="w-16" height="h-4" />
                  <Skeleton width="w-20" height="h-5" />
                </div>
              ))}
            </div>
            <Skeleton width="w-20" height="h-8" className="rounded" />
          </div>
        ))}
      </div>
    </div>
  );
}

export function FixedDepositsPage() {
  const {
    fixedDeposits,
    isLoading,
    error,
    refetch,
    createFixedDeposit,
    updateFixedDeposit,
    closeFixedDeposit,
  } = useFixedDeposits();

  const {
    bankAccounts,
    isLoading: isBankAccountsLoading,
    error: bankAccountsError,
    refetch: refetchBankAccounts,
  } = useBankAccounts();

  const [showCreateForm, setShowCreateForm] = useState(false);
  const [editingDeposit, setEditingDeposit] = useState<FixedDepositResponse | null>(null);
  const [depositToClose, setDepositToClose] = useState<FixedDepositResponse | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const activeDeposits = useMemo(
    () => fixedDeposits.filter((deposit) => deposit.status === 'Active' || deposit.status === 'Matured'),
    [fixedDeposits]
  );

  const committedPrincipal = useMemo(
    () => activeDeposits.reduce((sum, deposit) => sum + deposit.principal, 0),
    [activeDeposits]
  );

  const expectedInterestTotal = useMemo(
    () => activeDeposits.reduce((sum, deposit) => sum + deposit.expectedInterest, 0),
    [activeDeposits]
  );

  const handleOpenCreate = () => {
    setEditingDeposit(null);
    setShowCreateForm(true);
  };

  const handleEdit = (deposit: FixedDepositResponse) => {
    setShowCreateForm(false);
    setEditingDeposit(deposit);
  };

  const handleCloseClick = (deposit: FixedDepositResponse) => {
    setDepositToClose(deposit);
  };

  const handleConfirmClose = async () => {
    if (!depositToClose) {
      return;
    }

    const payload: CloseFixedDepositRequest = {
      actualInterest: depositToClose.expectedInterest,
      isEarlyWithdrawal: depositToClose.daysRemaining > 0,
    };

    await closeFixedDeposit(depositToClose.id, payload);
    setDepositToClose(null);
  };

  const handleCreateSubmit = async (data: CreateFixedDepositRequest) => {
    setIsSubmitting(true);
    try {
      await createFixedDeposit(data);
      setShowCreateForm(false);
      return true;
    } catch {
      return false;
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleEditSubmit = async (data: CreateFixedDepositRequest) => {
    if (!editingDeposit) {
      return false;
    }

    setIsSubmitting(true);
    try {
      await updateFixedDeposit(editingDeposit.id, {
        note: data.note,
      });
      setEditingDeposit(null);
      return true;
    } catch {
      return false;
    } finally {
      setIsSubmitting(false);
    }
  };

  if ((isLoading && fixedDeposits.length === 0) || (isBankAccountsLoading && bankAccounts.length === 0)) {
    return <FixedDepositsPageSkeleton />;
  }

  if (error) {
    return <ErrorDisplay message={error} onRetry={refetch} />;
  }

  if (bankAccountsError) {
    return <ErrorDisplay message={bankAccountsError} onRetry={refetchBankAccounts} />;
  }

  return (
    <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-8">
      <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
        <div>
          <h1 className="text-2xl font-bold text-[var(--text-primary)] mb-2">定存</h1>
          <p className="text-[var(--text-secondary)]">追蹤定存本金、到期日與預期利息</p>
        </div>
        <button
          type="button"
          onClick={handleOpenCreate}
          className="btn-accent inline-flex items-center gap-2"
        >
          <Plus className="w-5 h-5" />
          新增定存
        </button>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        <div className="metric-card metric-card-peach">
          <div className="flex items-center gap-3 mb-2">
            <div className="p-2 bg-[var(--accent-peach)] rounded-lg text-[var(--bg-primary)]">
              <Landmark className="w-5 h-5" />
            </div>
            <span className="text-sm font-medium text-[var(--accent-peach)] uppercase tracking-wider">
              定存本金（進行中）
            </span>
          </div>
          <div className="text-3xl font-bold text-[var(--text-primary)] mt-4 number-display">
            {formatCurrency(committedPrincipal, 'TWD')}
          </div>
        </div>

        <div className="metric-card metric-card-butter">
          <div className="flex items-center gap-3 mb-2">
            <div className="p-2 bg-[var(--accent-butter)] rounded-lg text-[var(--bg-primary)]">
              <Landmark className="w-5 h-5" />
            </div>
            <span className="text-sm font-medium text-[var(--accent-butter)] uppercase tracking-wider">
              預期利息（進行中）
            </span>
          </div>
          <div className="text-3xl font-bold text-[var(--text-primary)] mt-4 number-display">
            {formatCurrency(expectedInterestTotal, 'TWD')}
          </div>
        </div>
      </div>

      <FixedDepositList
        fixedDeposits={fixedDeposits}
        onEdit={handleEdit}
        onClose={handleCloseClick}
        emptyAction={
          <button
            type="button"
            onClick={handleOpenCreate}
            className="btn-accent inline-flex items-center gap-2"
          >
            <Plus className="w-4 h-4" />
            新增定存
          </button>
        }
      />

      {showCreateForm && (
        <FixedDepositForm
          bankAccounts={bankAccounts}
          onSubmit={handleCreateSubmit}
          onCancel={() => setShowCreateForm(false)}
          isLoading={isSubmitting}
        />
      )}

      {editingDeposit && (
        <FixedDepositForm
          bankAccounts={bankAccounts}
          initialData={editingDeposit}
          onSubmit={handleEditSubmit}
          onCancel={() => setEditingDeposit(null)}
          isLoading={isSubmitting}
        />
      )}

      <ConfirmationModal
        isOpen={Boolean(depositToClose)}
        onClose={() => setDepositToClose(null)}
        onConfirm={handleConfirmClose}
        title="結清定存"
        message={
          depositToClose ? (
            <span>
              確定要結清「{depositToClose.bankAccountName}」這筆定存嗎？
              <br />
              系統將以預期利息 {formatCurrency(depositToClose.expectedInterest, depositToClose.currency)} 作為實際利息。
            </span>
          ) : (
            ''
          )
        }
        confirmText="確認結清"
      />
    </div>
  );
}
