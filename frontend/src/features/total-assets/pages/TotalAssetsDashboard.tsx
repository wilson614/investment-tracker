import { useState } from 'react';
import { AssetCategorySummary } from '../components/AssetCategorySummary';
import { AssetsBreakdownPieChart } from '../components/AssetsBreakdownPieChart';
import { TotalAssetsBanner } from '../components/TotalAssetsBanner';
import { useTotalAssets } from '../hooks/useTotalAssets';
import { AllocationFormDialog } from '../../fund-allocations/components/AllocationFormDialog';
import { AllocationSummary, type AllocationSummaryItem } from '../../fund-allocations/components/AllocationSummary';
import { useFundAllocations } from '../../fund-allocations/hooks/useFundAllocations';

export function TotalAssetsDashboard() {
  const { summary: assetsData, isLoading } = useTotalAssets();
  const {
    allocations,
    unallocated,
    createAllocation,
    updateAllocation,
    deleteAllocation,
    error: allocationsError,
  } = useFundAllocations();
  const [editingAllocation, setEditingAllocation] = useState<AllocationSummaryItem | undefined>(undefined);
  const [isDialogOpen, setIsDialogOpen] = useState(false);

  const handleAllocationEdit = (allocation: AllocationSummaryItem) => {
    setEditingAllocation(allocation);
    setIsDialogOpen(true);
  };

  const handleAllocationDelete = async (id: string) => {
    if (editingAllocation?.id === id) {
      setEditingAllocation(undefined);
    }

    try {
      await deleteAllocation(id);
    } catch {
      // Error is handled by the mutation's onError callback
    }
  };

  const allocationItems: AllocationSummaryItem[] = allocations.map((allocation) => ({
    id: allocation.id,
    purpose: allocation.purpose,
    amount: allocation.amount,
    note: allocation.note,
  }));

  return (
    <div className="space-y-6 max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
      <div className="flex justify-between items-center">
        <div>
          <h1 className="text-2xl font-bold text-[var(--text-primary)]">總資產儀表板</h1>
          <p className="text-[var(--text-muted)] mt-1">查看您的所有投資與銀行存款總覽</p>
        </div>
      </div>

      {/* Top Banner - Grand Total */}
      <TotalAssetsBanner data={assetsData} isLoading={isLoading} />

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Left Column - Category Summaries */}
        <div className="lg:col-span-2 space-y-6">
          <h2 className="text-lg font-semibold text-[var(--text-primary)]">資產類別</h2>
          <AssetCategorySummary data={assetsData} isLoading={isLoading} />

          {/* Interest Summary Card (Future enhancement) */}
          {!isLoading && (assetsData?.totalMonthlyInterest ?? 0) > 0 && (
            <div className="card-dark p-6 bg-gradient-to-r from-blue-900/20 to-transparent border-blue-900/30">
              <h3 className="text-blue-400 font-medium mb-2">預估利息收益</h3>
              <div className="flex gap-8">
                <div>
                  <p className="text-xs text-[var(--text-muted)] uppercase">每月預估</p>
                  <p className="text-xl font-bold text-[var(--text-primary)] font-mono">
                    NT$ {Math.round(assetsData?.totalMonthlyInterest ?? 0).toLocaleString('zh-TW')}
                  </p>
                </div>
                <div>
                  <p className="text-xs text-[var(--text-muted)] uppercase">每年預估</p>
                  <p className="text-xl font-bold text-[var(--text-primary)] font-mono">
                    NT$ {Math.round(assetsData?.totalYearlyInterest ?? 0).toLocaleString('zh-TW')}
                  </p>
                </div>
              </div>
            </div>
          )}
        </div>

        {/* Right Column - Pie Chart */}
        <div className="lg:col-span-1">
          <AssetsBreakdownPieChart data={assetsData} isLoading={isLoading} />
        </div>
      </div>

      <section className="space-y-4">
        <div className="flex items-start justify-between gap-4">
          <div>
            <h2 className="text-lg font-semibold text-[var(--text-primary)]">銀行資金配置</h2>
            <p className="text-sm text-[var(--text-muted)] mt-1">管理銀行資產用途與未配置金額</p>
          </div>
          <button
            type="button"
            onClick={() => {
              setEditingAllocation(undefined);
              setIsDialogOpen(true);
            }}
            className="btn-accent px-4 py-2 whitespace-nowrap"
          >
            新增配置
          </button>
        </div>

        {allocationsError ? (
          <div className="p-3 rounded border border-red-500/40 bg-red-500/10 text-red-200 text-sm" role="alert">
            {allocationsError}
          </div>
        ) : null}

        <AllocationSummary
          allocations={allocationItems}
          bankTotal={assetsData?.bankTotal ?? 0}
          unallocatedAmount={unallocated}
          onEdit={handleAllocationEdit}
          onDelete={(id) => {
            void handleAllocationDelete(id);
          }}
        />
      </section>

      <AllocationFormDialog
        isOpen={isDialogOpen}
        onClose={() => {
          setIsDialogOpen(false);
          setEditingAllocation(undefined);
        }}
        onSubmit={async (data) => {
          if (editingAllocation) {
            await updateAllocation(editingAllocation.id, data);
          } else {
            await createAllocation(data);
          }

          setIsDialogOpen(false);
          setEditingAllocation(undefined);
        }}
        initialData={editingAllocation}
      />
    </div>
  );
}
