import { useState } from 'react';
import { Plus } from 'lucide-react';
import { AssetsBreakdownPieChart } from '../components/AssetsBreakdownPieChart';
import { CoreMetricsSection } from '../components/CoreMetricsSection';
import { DisposableAssetsSection } from '../components/DisposableAssetsSection';
import { NonDisposableAssetsSection } from '../components/NonDisposableAssetsSection';
import { useTotalAssets } from '../hooks/useTotalAssets';
import { useFundAllocations } from '../../fund-allocations/hooks/useFundAllocations';
import { AllocationSummary, type AllocationSummaryItem } from '../../fund-allocations/components/AllocationSummary';
import { AllocationFormDialog } from '../../fund-allocations/components/AllocationFormDialog';
import { Skeleton } from '../../../components/common/SkeletonLoader';
import { ConfirmationModal } from '../../../components/modals/ConfirmationModal';
import { formatCurrency } from '../../../utils/currency';

export function TotalAssetsDashboard() {
  const { summary: assetsData, isLoading } = useTotalAssets();
  const {
    allocations,
    error: allocationsError,
    isLoading: isAllocationsLoading,
    createAllocation,
    updateAllocation,
    deleteAllocation,
  } = useFundAllocations();

  const [isFormOpen, setIsFormOpen] = useState(false);
  const [editingAllocation, setEditingAllocation] = useState<AllocationSummaryItem | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<string | null>(null);

  const nonDisposableAllocationCount = allocations.filter((allocation) => !allocation.isDisposable).length;

  const allocationItems: AllocationSummaryItem[] = allocations.map((allocation) => ({
    id: allocation.id,
    purpose: allocation.purpose,
    amount: allocation.amount,
    isDisposable: allocation.isDisposable,
  }));

  const handleOpenCreate = () => {
    setEditingAllocation(null);
    setIsFormOpen(true);
  };

  const handleEdit = (allocation: AllocationSummaryItem) => {
    setEditingAllocation(allocation);
    setIsFormOpen(true);
  };

  const handleDelete = (id: string) => {
    setDeleteTarget(id);
  };

  const handleFormSubmit = async (data: {
    purpose: string;
    amount: number;
    isDisposable: boolean;
  }) => {
    if (editingAllocation) {
      await updateAllocation(editingAllocation.id, {
        purpose: data.purpose,
        amount: data.amount,
        isDisposable: data.isDisposable,
      });
    } else {
      await createAllocation({
        purpose: data.purpose,
        amount: data.amount,
        isDisposable: data.isDisposable,
      });
    }
    setIsFormOpen(false);
    setEditingAllocation(null);
  };

  const totalAssets = assetsData?.grandTotal ?? 0;
  const bankTotal = assetsData?.bankTotal ?? 0;
  const unallocatedAmount = assetsData?.unallocated ?? 0;

  const investmentTotal = assetsData?.investmentTotal ?? 0;
  const disposableDeposit = assetsData?.disposableDeposit ?? 0;
  const investmentRatioDenominator = investmentTotal + disposableDeposit;
  const correctedInvestmentRatio =
    investmentRatioDenominator > 0 ? investmentTotal / investmentRatioDenominator : 0;
  const isPageLoading = isLoading || isAllocationsLoading;

  if (isPageLoading) {
    return (
      <div className="space-y-6 max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
        <h1 className="text-2xl font-bold text-[var(--text-primary)]">
          <Skeleton width="w-40" height="h-8" />
        </h1>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-6 items-stretch">
          <section className="card-dark p-6 h-full min-h-[170px] flex flex-col justify-center">
            <Skeleton width="w-16" height="h-7" />
            <Skeleton width="w-52" height="h-10" className="mt-1" />
          </section>

          <section className="card-dark p-3 sm:p-4 space-y-2 w-full h-full min-h-[170px]">
            <Skeleton width="w-32" height="h-7" />
            <div className="space-y-2.5">
              {[1, 2].map((row) => (
                <div key={row} className="flex items-center gap-3">
                  <Skeleton width="w-28" height="h-5" />
                  <Skeleton width="w-full" height="h-2" />
                  <Skeleton width="w-12" height="h-5" />
                </div>
              ))}
            </div>
          </section>
        </div>

        <div className="card-dark p-6 h-[400px]">
          <Skeleton width="w-36" height="h-7" className="mb-4" />
          <div className="w-full h-[320px] flex flex-col items-center justify-center">
            <Skeleton width="w-48" height="h-48" circle />
            <div className="mt-4 flex gap-4">
              <Skeleton width="w-20" height="h-4" />
              <Skeleton width="w-20" height="h-4" />
              <Skeleton width="w-20" height="h-4" />
              <Skeleton width="w-20" height="h-4" />
            </div>
          </div>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-3 gap-6 items-stretch">
          <div className="lg:col-span-2 h-full">
            <section className="card-dark p-6 space-y-5 min-h-[200px] lg:min-h-[280px] h-full flex flex-col">
              <header className="space-y-1">
                <Skeleton width="w-28" height="h-7" />
                <Skeleton width="w-full" height="h-5" />
              </header>

              <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                {[1, 2, 3].map((item) => (
                  <div key={item} className="rounded-lg border border-[var(--border-color)] p-4 bg-[var(--bg-tertiary)]/50">
                    <div className="flex items-start justify-between gap-2">
                      <Skeleton width="w-20" height="h-5" />
                      <Skeleton width="w-4" height="h-4" />
                    </div>
                    <Skeleton width="w-full" height="h-8" className="mt-2" />
                    <Skeleton width="w-20" height="h-5" className="mt-2" />
                  </div>
                ))}
              </div>
            </section>
          </div>

          <div className="lg:col-span-1 h-full">
            <section className="card-dark p-6 h-full flex flex-col min-h-[200px] lg:min-h-[280px]">
              <header className="space-y-1">
                <Skeleton width="w-32" height="h-7" />
                <Skeleton width="w-full" height="h-5" />
              </header>

              <div className="flex-1 flex flex-col justify-center mt-4">
                <div className="rounded-lg border border-[var(--border-color)] p-4 bg-[var(--bg-tertiary)]/50 space-y-2">
                  <Skeleton width="w-28" height="h-5" />
                  <Skeleton width="w-full" height="h-8" />
                  <Skeleton width="w-20" height="h-5" />
                </div>
              </div>

              <Skeleton width="w-28" height="h-5" className="mt-4" />
            </section>
          </div>
        </div>

        <div id="allocation-management-section" className="space-y-4 scroll-mt-24">
          <div className="flex items-center justify-between">
            <Skeleton width="w-36" height="h-7" />
            <Skeleton width="w-24" height="h-8" />
          </div>

          <div className="card-dark p-6 space-y-5">
            <div className="grid grid-cols-2 gap-4 mb-5">
              {[1, 2].map((summaryCard) => (
                <div
                  key={summaryCard}
                  className="rounded-lg bg-[var(--bg-tertiary)]/50 p-4 text-center"
                >
                  <Skeleton width="w-20" height="h-5" className="mb-1" />
                  <Skeleton width="w-full" height="h-8" />
                </div>
              ))}
            </div>

            <div className="space-y-2">
              {[1, 2, 3].map((item) => (
                <div
                  key={item}
                  className="border border-[var(--border-color)] rounded-lg p-3 bg-[var(--bg-tertiary)]/50"
                >
                  <div className="flex items-center justify-between gap-3">
                    <div className="space-y-2 min-w-0 flex-1">
                      <Skeleton width="w-32" height="h-5" />
                      <Skeleton width="w-28" height="h-6" />
                    </div>

                    <div className="flex items-center gap-1 shrink-0">
                      <Skeleton width="w-8" height="h-8" />
                      <Skeleton width="w-8" height="h-8" />
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-6 max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
      <h1 className="text-2xl font-bold text-[var(--text-primary)]">總資產儀表板</h1>

      {/* ROW 1: 總金額 + 資金配置效率 並排 */}
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-6 items-stretch">
        <section className="card-dark p-6 h-full min-h-[170px] flex flex-col justify-center">
          <p className="text-lg font-semibold text-[var(--text-muted)]">總資產</p>
          <p className="mt-1 text-3xl sm:text-4xl font-bold font-mono text-[var(--text-primary)]">
            {formatCurrency(totalAssets, 'TWD')}
          </p>
        </section>

        <CoreMetricsSection
          data={{
            investmentRatio: correctedInvestmentRatio,
            stockRatio: assetsData?.stockRatio ?? 0,
          }}
        />
      </div>

      {/* ROW 2: 圓餅圖獨立 */}
      <AssetsBreakdownPieChart
        portfolioMarketValue={investmentTotal}
        cashBalance={assetsData?.cashBalance ?? 0}
        disposableDeposit={disposableDeposit}
        nonDisposableDeposit={assetsData?.nonDisposableDeposit ?? 0}
        isLoading={isLoading}
      />

      {/* ROW 3: 可動用 + 不可動用（維持原本配置） */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6 items-stretch">
        <div className="lg:col-span-2 h-full">
          <DisposableAssetsSection
            portfolioValue={assetsData?.portfolioValue ?? 0}
            cashBalance={assetsData?.cashBalance ?? 0}
            disposableDeposit={assetsData?.disposableDeposit ?? 0}
            investmentTotal={assetsData?.investmentTotal ?? 0}
          />
        </div>

        <div className="lg:col-span-1 h-full">
          <NonDisposableAssetsSection
            nonDisposableDeposit={assetsData?.nonDisposableDeposit ?? 0}
            allocationCount={nonDisposableAllocationCount}
          />
        </div>
      </div>

      {/* 資金配置管理區塊 */}
      {allocationsError ? (
        <div className="p-3 rounded border border-red-500/40 bg-red-500/10 text-red-200 text-sm" role="alert">
          {allocationsError}
        </div>
      ) : null}

      <div id="allocation-management-section" className="space-y-4 scroll-mt-24">
        <div className="flex items-center justify-between">
          <h2 className="text-lg font-semibold text-[var(--text-primary)]">資金配置管理</h2>
          <button
            type="button"
            onClick={handleOpenCreate}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium rounded-lg bg-[var(--accent-peach)] text-[var(--bg-primary)] hover:opacity-90 transition-opacity"
          >
            <Plus size={16} />
            新增配置
          </button>
        </div>

        <AllocationSummary
          allocations={allocationItems}
          bankTotal={bankTotal}
          unallocatedAmount={unallocatedAmount}
          onEdit={handleEdit}
          onDelete={handleDelete}
        />
      </div>

      {/* 新增/編輯配置彈窗 */}
      <AllocationFormDialog
        isOpen={isFormOpen}
        onClose={() => {
          setIsFormOpen(false);
          setEditingAllocation(null);
        }}
        onSubmit={handleFormSubmit}
        initialData={
          editingAllocation
            ? {
                purpose: editingAllocation.purpose,
                amount: editingAllocation.amount,
                isDisposable: editingAllocation.isDisposable,
              }
            : undefined
        }
      />

      <ConfirmationModal
        isOpen={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={() => {
          if (deleteTarget) {
            deleteAllocation(deleteTarget);
            setDeleteTarget(null);
          }
        }}
        title="刪除配置"
        message="確定要刪除此資金配置嗎？此動作無法復原。"
        confirmText="刪除"
        isDestructive={true}
      />
    </div>
  );
}
