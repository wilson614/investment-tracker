import { useState } from 'react';
import { Plus, Wallet, AlertCircle } from 'lucide-react';
import { useBankAccounts } from '../hooks/useBankAccounts';
import { BankAccountCard } from '../components/bank-accounts/BankAccountCard';
import { BankAccountForm } from '../components/bank-accounts/BankAccountForm';
import { LoadingSpinner, ErrorDisplay } from '../components/common';
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

  const [showForm, setShowForm] = useState(false);
  const [editingAccount, setEditingAccount] = useState<BankAccount | undefined>(undefined);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const totalAssets = bankAccounts.reduce((sum, acc) => sum + acc.totalAssets, 0);
  const totalYearlyInterest = bankAccounts.reduce((sum, acc) => sum + acc.yearlyInterest, 0);
  const totalMonthlyInterest = bankAccounts.reduce((sum, acc) => sum + acc.monthlyInterest, 0);

  const handleCreate = () => {
    setEditingAccount(undefined);
    setShowForm(true);
  };

  const handleEdit = (account: BankAccount) => {
    setEditingAccount(account);
    setShowForm(true);
  };

  const handleSubmit = async (data: CreateBankAccountRequest | UpdateBankAccountRequest) => {
    setIsSubmitting(true);
    let success = false;
    if (editingAccount) {
      success = await updateBankAccount(editingAccount.id, data as UpdateBankAccountRequest);
    } else {
      success = await createBankAccount(data as CreateBankAccountRequest);
    }
    setIsSubmitting(false);

    if (success) {
      setShowForm(false);
    }
    return success;
  };

  const formatCurrency = (val: number) => {
    return new Intl.NumberFormat('zh-TW', {
      style: 'currency',
      currency: 'TWD',
      maximumFractionDigits: 0
    }).format(val);
  };

  if (isLoading && bankAccounts.length === 0) {
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
          <h1 className="text-display font-bold text-[var(--text-primary)] mb-2">銀行帳戶</h1>
          <p className="text-[var(--text-secondary)]">管理台幣高利活存與定存帳戶</p>
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
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        <div className="metric-card metric-card-peach">
          <div className="flex items-center gap-3 mb-2">
            <div className="p-2 bg-[var(--accent-peach)] rounded-lg text-[var(--bg-primary)]">
              <Wallet className="w-5 h-5" />
            </div>
            <span className="text-sm font-medium text-[var(--accent-peach)] uppercase tracking-wider">
              總資產
            </span>
          </div>
          <div className="text-2xl font-bold text-[var(--text-primary)]">
            {formatCurrency(totalAssets)}
          </div>
        </div>

        <div className="metric-card metric-card-cream">
          <div className="flex items-center gap-3 mb-2">
            <span className="text-sm font-medium text-[var(--accent-cream)] uppercase tracking-wider">
              預估年利息
            </span>
          </div>
          <div className="text-2xl font-bold text-[var(--text-primary)]">
            {formatCurrency(totalYearlyInterest)}
          </div>
        </div>

        <div className="metric-card metric-card-sand">
          <div className="flex items-center gap-3 mb-2">
            <span className="text-sm font-medium text-[var(--accent-sand)] uppercase tracking-wider">
              平均月利息
            </span>
          </div>
          <div className="text-2xl font-bold text-[var(--text-primary)]">
            {formatCurrency(totalMonthlyInterest)}
          </div>
        </div>
      </div>

      {/* Account Grid */}
      {bankAccounts.length === 0 ? (
        <div className="text-center py-12 bg-[var(--bg-secondary)] rounded-xl border border-dashed border-[var(--border-color)]">
          <AlertCircle className="w-12 h-12 text-[var(--text-muted)] mx-auto mb-4" />
          <h3 className="text-lg font-medium text-[var(--text-secondary)] mb-2">尚無銀行帳戶</h3>
          <p className="text-[var(--text-muted)] mb-6">新增您的第一個銀行帳戶以開始追蹤利息收益</p>
          <button
            onClick={handleCreate}
            className="btn-dark inline-flex items-center gap-2"
          >
            <Plus className="w-4 h-4" />
            新增帳戶
          </button>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
          {bankAccounts.map((account) => (
            <BankAccountCard
              key={account.id}
              account={account}
              onEdit={handleEdit}
              onDelete={deleteBankAccount}
            />
          ))}
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
    </div>
  );
}
