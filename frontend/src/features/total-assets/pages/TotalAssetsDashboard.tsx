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
import { formatCurrency } from '../../../utils/currency';

export function TotalAssetsDashboard() {
  const { summary: assetsData, isLoading } = useTotalAssets();
  const {
    allocations,
    error: allocationsError,
    createAllocation,
    updateAllocation,
    deleteAllocation,
  } = useFundAllocations();

  const [isFormOpen, setIsFormOpen] = useState(false);
  const [editingAllocation, setEditingAllocation] = useState<AllocationSummaryItem | null>(null);

  const nonDisposableAllocations = allocations
    .filter((allocation) => !allocation.isDisposable)
    .map((allocation) => ({
      id: allocation.id,
      purposeDisplayName: allocation.purposeDisplayName,
      amount: allocation.amount,
    }));

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

  const handleDelete = async (id: string) => {
    if (window.confirm('確定要刪除此資金配置嗎？')) {
      await deleteAllocation(id);
    }
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

  return (
    <div className="space-y-6 max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
      {/* 頂部：標題 + 總資產金額 */}
      <div className="card-dark p-6">
        <p className="text-sm text-[var(--text-muted)]">總資產儀表板</p>
        <p className="text-3xl sm:text-4xl font-bold font-mono text-[var(--text-primary)] mt-1">
          {formatCurrency(totalAssets, 'TWD')}
        </p>
      </div>

      {/* 核心指標：投資比例 + 股票佔比 */}
      <CoreMetricsSection
        data={{
          investmentRatio: assetsData?.investmentRatio ?? 0,
          stockRatio: assetsData?.stockRatio ?? 0,
        }}
      />

      {/* 圓餅圖：獨立全寬一行 */}
      <AssetsBreakdownPieChart
        portfolioMarketValue={assetsData?.investmentTotal ?? 0}
        cashBalance={assetsData?.cashBalance ?? 0}
        disposableDeposit={assetsData?.disposableDeposit ?? 0}
        nonDisposableDeposit={assetsData?.nonDisposableDeposit ?? 0}
        isLoading={isLoading}
      />

      {/* 下方左右兩欄：可動用資產 2/3、不可動用資產 1/3 */}
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
            nonDisposableAllocations={nonDisposableAllocations}
          />
        </div>
      </div>

      {/* 資金配置管理區塊 */}
      {allocationsError ? (
        <div className="p-3 rounded border border-red-500/40 bg-red-500/10 text-red-200 text-sm" role="alert">
          {allocationsError}
        </div>
      ) : null}

      <div className="space-y-4">
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
    </div>
  );
}
