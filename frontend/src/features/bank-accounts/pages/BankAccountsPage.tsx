import { useState } from 'react';
import { Plus, Wallet, AlertCircle } from 'lucide-react';
import { useBankAccounts } from '../hooks/useBankAccounts';
import { useTotalAssets } from '../../total-assets/hooks/useTotalAssets';
import { BankAccountCard } from '../components/BankAccountCard';
import { BankAccountForm } from '../components/BankAccountForm';
import { InterestEstimationCard } from '../components/InterestEstimationCard';
import { LoadingSpinner, ErrorDisplay } from '../../../components/common';
import { ConfirmationModal } from '../../../components/modals/ConfirmationModal';
import { formatCurrency } from '../../../utils/currency';
import type { BankAccount, CreateBankAccountRequest, UpdateBankAccountRequest } from '../types';

export function BankAccountsPage() {
  const {
    bankAccounts,
    isLoading,
    error,
    createBankAccount,
    updateBankAccount,
    deleteBankAccount,
    refetch
  } = useBankAccounts();

  const { summary: assetsSummary, isLoading: isAssetsLoading } = useTotalAssets();

  const [showForm, setShowForm] = useState(false);
  const [editingAccount, setEditingAccount] = useState<BankAccount | undefined>(undefined);
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Modal state
  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [deletingAccountId, setDeletingAccountId] = useState<string | null>(null);

  const totalAssets = assetsSummary?.bankTotal ?? 0;
  const totalYearlyInterest = assetsSummary?.totalYearlyInterest ?? 0;
  const totalMonthlyInterest = assetsSummary?.totalMonthlyInterest ?? 0;

  const handleCreate = () => {
    setEditingAccount(undefined);
    setShowForm(true);
  };

  const handleEdit = (account: BankAccount) => {
    setEditingAccount(account);
    setShowForm(true);
  };

  const handleDeleteClick = (id: string) => {
    setDeletingAccountId(id);
    setShowDeleteModal(true);
  };

  const handleConfirmDelete = async () => {
    if (deletingAccountId) {
      await deleteBankAccount(deletingAccountId);
      setDeletingAccountId(null);
    }
  };

  const handleSubmit = async (data: CreateBankAccountRequest | UpdateBankAccountRequest) => {
    setIsSubmitting(true);
    try {
      if (editingAccount) {
        await updateBankAccount(editingAccount.id, data as UpdateBankAccountRequest);
      } else {
        await createBankAccount(data as CreateBankAccountRequest);
      }
      setShowForm(false);
      return true;
    } catch {
      // Error is handled by hook's onError toast
      return false;
    } finally {
      setIsSubmitting(false);
    }
  };


  if ((isLoading && bankAccounts.length === 0) || (isAssetsLoading && !assetsSummary)) {
    return <LoadingSpinner />;
  }

  if (error) {
    return <ErrorDisplay message={error} onRetry={refetch} />;
  }

  return (
    <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-8">
      {/* Header & Stats */}
      <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
        <div>
          <h1 className="text-2xl font-bold text-[var(--text-primary)] mb-2">銀行帳戶</h1>
          <p className="text-[var(--text-secondary)]">管理您的銀行存款帳戶</p>
        </div>
        <button
          onClick={handleCreate}
          className="btn-accent flex items-center gap-2"
        >
          <Plus className="w-5 h-5" />
          新增帳戶
        </button>
      </div>

      {/* Summary Cards */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        <div className="metric-card metric-card-peach">
          <div className="flex items-center gap-3 mb-2">
            <div className="p-2 bg-[var(--accent-peach)] rounded-lg text-[var(--bg-primary)]">
              <Wallet className="w-5 h-5" />
            </div>
            <span className="text-sm font-medium text-[var(--accent-peach)] uppercase tracking-wider">
              總資產
            </span>
          </div>
          <div className="text-3xl font-bold text-[var(--text-primary)] mt-4">
            {formatCurrency(totalAssets, 'TWD')}
          </div>
        </div>

        <InterestEstimationCard
          yearlyInterest={totalYearlyInterest}
          monthlyInterest={totalMonthlyInterest}
        />
      </div>

      {/* Account Grid */}
      {bankAccounts.length === 0 ? (
        <div className="text-center py-12 bg-[var(--bg-secondary)] rounded-xl border border-dashed border-[var(--border-color)]">
          <AlertCircle className="w-12 h-12 text-[var(--text-muted)] mx-auto mb-4" />
          <h3 className="text-lg font-medium text-[var(--text-secondary)] mb-2">尚無銀行帳戶</h3>
          <p className="text-[var(--text-muted)] mb-6">新增您的第一個銀行帳戶以開始追蹤利息收益</p>
          <button
            onClick={handleCreate}
            className="btn-accent inline-flex items-center gap-2"
          >
            <Plus className="w-4 h-4" />
            新增帳戶
          </button>
        </div>
      ) : (
        <div className="space-y-4">
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
            {bankAccounts.map((account) => (
              <BankAccountCard
                key={account.id}
                account={account}
                onEdit={handleEdit}
                onDelete={handleDeleteClick}
                showCurrencyBadge
              />
            ))}
          </div>
        </div>
      )}

      {/* Form Modal */}
      {showForm && (
        <BankAccountForm
          initialData={editingAccount}
          onSubmit={handleSubmit}
          onCancel={() => setShowForm(false)}
          isLoading={isSubmitting}
        />
      )}

      {/* Delete Confirmation Modal */}
      <ConfirmationModal
        isOpen={showDeleteModal}
        onClose={() => setShowDeleteModal(false)}
        onConfirm={handleConfirmDelete}
        title="刪除銀行帳戶"
        message="確定要刪除此銀行帳戶嗎？此動作無法復原。"
        confirmText="刪除"
        isDestructive={true}
      />
    </div>
  );
}
