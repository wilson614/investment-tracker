import { useRef, useState } from 'react';
import { Plus, Wallet, AlertCircle, Landmark, CheckCircle2, XCircle } from 'lucide-react';
import { useBankAccounts } from '../hooks/useBankAccounts';
import { useTotalAssets } from '../../total-assets/hooks/useTotalAssets';
import { BankAccountCard } from '../components/BankAccountCard';
import { BankAccountForm } from '../components/BankAccountForm';
import { InterestEstimationCard } from '../components/InterestEstimationCard';
import { LoadingSpinner, ErrorDisplay } from '../../../components/common';
import { FileDropdown } from '../../../components/common/FileDropdown';
import { ConfirmationModal } from '../../../components/modals/ConfirmationModal';
import { BankAccountImportButton, BankAccountImportModal } from '../../../components/import';
import { exportBankAccountsToCSV } from '../../../services/csvExport';
import { formatCurrency } from '../../../utils/currency';
import type { BankAccount, CreateBankAccountRequest, UpdateBankAccountRequest } from '../types';

export function BankAccountsPage() {
  const {
    bankAccounts,
    isLoading,
    error,
    createBankAccount,
    updateBankAccount,
    closeBankAccount,
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
  const [showCloseModal, setShowCloseModal] = useState(false);
  const [closingAccount, setClosingAccount] = useState<BankAccount | null>(null);
  const [showImportModal, setShowImportModal] = useState(false);
  const [selectedImportFile, setSelectedImportFile] = useState<File | null>(null);
  const importTriggerRef = useRef<(() => void) | null>(null);

  const totalAssets = assetsSummary?.bankTotal ?? 0;
  const totalYearlyInterest = assetsSummary?.totalYearlyInterest ?? 0;
  const totalMonthlyInterest = assetsSummary?.totalMonthlyInterest ?? 0;

  const savingsAccounts = bankAccounts.filter((account) => account.accountType === 'Savings');
  const fixedDepositAccounts = bankAccounts.filter((account) => account.accountType === 'FixedDeposit');

  const activeFixedDeposits = fixedDepositAccounts.filter((account) =>
    account.fixedDepositStatus === 'Active' || account.fixedDepositStatus === 'Matured'
  );
  const fixedDepositPrincipal = activeFixedDeposits.reduce((sum, account) => sum + account.totalAssets, 0);
  const expectedInterestTotal = activeFixedDeposits.reduce((sum, account) => sum + (account.expectedInterest ?? 0), 0);

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

  const handleCloseClick = (account: BankAccount) => {
    setClosingAccount(account);
    setShowCloseModal(true);
  };

  const handleConfirmDelete = async () => {
    if (deletingAccountId) {
      await deleteBankAccount(deletingAccountId);
      setDeletingAccountId(null);
      setShowDeleteModal(false);
    }
  };

  const handleConfirmClose = async () => {
    if (!closingAccount) {
      return;
    }

    await closeBankAccount(closingAccount.id, {
      actualInterest: closingAccount.expectedInterest,
    });
    setClosingAccount(null);
    setShowCloseModal(false);
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

  const handleExport = () => {
    if (bankAccounts.length === 0) return;
    exportBankAccountsToCSV(bankAccounts);
  };

  const handleFileSelected = (file: File) => {
    setSelectedImportFile(file);
    setShowImportModal(true);
  };

  const handleCloseImportModal = () => {
    setShowImportModal(false);
    setSelectedImportFile(null);
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
          <p className="text-[var(--text-secondary)]">管理您的活存與定存帳戶</p>
        </div>
        <div className="flex items-center gap-2">
          <FileDropdown
            onImport={() => importTriggerRef.current?.()}
            onExport={handleExport}
            exportDisabled={bankAccounts.length === 0}
          />
          <button
            onClick={handleCreate}
            className="btn-accent flex items-center gap-2"
          >
            <Plus className="w-5 h-5" />
            新增帳戶
          </button>
        </div>
      </div>

      {/* Summary Cards */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6">
        <div className="metric-card metric-card-peach">
          <div className="flex items-center gap-3 mb-2">
            <div className="p-2 bg-[var(--accent-peach)] rounded-lg text-[var(--bg-primary)]">
              <Wallet className="w-5 h-5" />
            </div>
            <span className="text-sm font-medium text-[var(--accent-peach)] uppercase tracking-wider">
              銀行總資產
            </span>
          </div>
          <div className="text-3xl font-bold text-[var(--text-primary)] mt-4">
            {formatCurrency(totalAssets, 'TWD')}
          </div>
        </div>

        <div className="metric-card metric-card-butter">
          <div className="flex items-center gap-3 mb-2">
            <div className="p-2 bg-[var(--accent-butter)] rounded-lg text-[var(--bg-primary)]">
              <Landmark className="w-5 h-5" />
            </div>
            <span className="text-sm font-medium text-[var(--accent-butter)] uppercase tracking-wider">
              定存本金（進行中）
            </span>
          </div>
          <div className="text-3xl font-bold text-[var(--text-primary)] mt-4 number-display">
            {formatCurrency(fixedDepositPrincipal, 'TWD')}
          </div>
        </div>

        <div className="metric-card metric-card-cream">
          <div className="flex items-center gap-3 mb-2">
            <div className="p-2 bg-[var(--accent-cream)] rounded-lg text-[var(--bg-primary)]">
              <CheckCircle2 className="w-5 h-5" />
            </div>
            <span className="text-sm font-medium text-[var(--accent-cream)] uppercase tracking-wider">
              預期利息（進行中）
            </span>
          </div>
          <div className="text-3xl font-bold text-[var(--text-primary)] mt-4 number-display">
            {formatCurrency(expectedInterestTotal, 'TWD')}
          </div>
        </div>

        <InterestEstimationCard
          yearlyInterest={totalYearlyInterest}
          monthlyInterest={totalMonthlyInterest}
        />
      </div>

      {/* Savings Accounts */}
      <section className="space-y-4">
        <div className="flex items-center justify-between">
          <h2 className="text-xl font-bold text-[var(--text-primary)]">活存帳戶</h2>
          <span className="text-sm text-[var(--text-muted)]">{savingsAccounts.length} 筆</span>
        </div>

        {savingsAccounts.length === 0 ? (
          <div className="text-center py-8 bg-[var(--bg-secondary)] rounded-xl border border-dashed border-[var(--border-color)]">
            <AlertCircle className="w-10 h-10 text-[var(--text-muted)] mx-auto mb-3" />
            <p className="text-[var(--text-secondary)]">目前沒有活存帳戶</p>
          </div>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
            {savingsAccounts.map((account) => (
              <BankAccountCard
                key={account.id}
                account={account}
                onEdit={handleEdit}
                onDelete={handleDeleteClick}
                showCurrencyBadge
              />
            ))}
          </div>
        )}
      </section>

      {/* Fixed Deposits */}
      <section className="space-y-4">
        <div className="flex items-center justify-between">
          <h2 className="text-xl font-bold text-[var(--text-primary)]">定存帳戶</h2>
          <span className="text-sm text-[var(--text-muted)]">{fixedDepositAccounts.length} 筆</span>
        </div>

        {fixedDepositAccounts.length === 0 ? (
          <div className="text-center py-8 bg-[var(--bg-secondary)] rounded-xl border border-dashed border-[var(--border-color)]">
            <XCircle className="w-10 h-10 text-[var(--text-muted)] mx-auto mb-3" />
            <p className="text-[var(--text-secondary)]">目前沒有定存帳戶</p>
          </div>
        ) : (
          <div className="space-y-4">
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
              {fixedDepositAccounts.map((account) => (
                <div key={account.id} className="space-y-3">
                  <BankAccountCard
                    account={account}
                    onEdit={handleEdit}
                    onDelete={handleDeleteClick}
                    showCurrencyBadge
                  />

                  {(account.fixedDepositStatus === 'Active' || account.fixedDepositStatus === 'Matured') && (
                    <button
                      type="button"
                      onClick={() => handleCloseClick(account)}
                      className="w-full btn-dark border border-[var(--accent-butter)]/40 hover:bg-[var(--accent-butter)]/10"
                    >
                      結清定存
                    </button>
                  )}
                </div>
              ))}
            </div>
          </div>
        )}
      </section>

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
        onClose={() => {
          setShowDeleteModal(false);
          setDeletingAccountId(null);
        }}
        onConfirm={() => {
          void handleConfirmDelete();
        }}
        title="刪除銀行帳戶"
        message="確定要刪除此銀行帳戶嗎？此動作無法復原。"
        confirmText="刪除"
        isDestructive={true}
      />

      <ConfirmationModal
        isOpen={showCloseModal}
        onClose={() => {
          setShowCloseModal(false);
          setClosingAccount(null);
        }}
        onConfirm={() => {
          void handleConfirmClose();
        }}
        title="結清定存"
        message={
          closingAccount ? (
            <span>
              確定要結清「{closingAccount.bankName}」這筆定存嗎？
              <br />
              系統將以預期利息 {formatCurrency(closingAccount.expectedInterest ?? 0, closingAccount.currency)} 作為實際利息。
            </span>
          ) : (
            ''
          )
        }
        confirmText="確認結清"
      />

      {selectedImportFile && (
        <BankAccountImportModal
          isOpen={showImportModal}
          onClose={handleCloseImportModal}
          file={selectedImportFile}
        />
      )}

      <BankAccountImportButton
        onFileSelected={handleFileSelected}
        renderTrigger={(onClick) => {
          importTriggerRef.current = onClick;
          return null;
        }}
      />
    </div>
  );
}
