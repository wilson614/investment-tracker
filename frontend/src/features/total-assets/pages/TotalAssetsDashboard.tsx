import { AssetsBreakdownPieChart } from '../components/AssetsBreakdownPieChart';
import { CoreMetricsSection } from '../components/CoreMetricsSection';
import { DisposableAssetsSection } from '../components/DisposableAssetsSection';
import { NonDisposableAssetsSection } from '../components/NonDisposableAssetsSection';
import { useTotalAssets } from '../hooks/useTotalAssets';
import { useFundAllocations } from '../../fund-allocations/hooks/useFundAllocations';

export function TotalAssetsDashboard() {
  const { summary: assetsData, isLoading } = useTotalAssets();
  const { allocations, error: allocationsError } = useFundAllocations();

  const nonDisposableAllocations = allocations
    .filter((allocation) => !allocation.isDisposable)
    .map((allocation) => ({
      id: allocation.id,
      purposeDisplayName: allocation.purposeDisplayName,
      amount: allocation.amount,
    }));

  return (
    <div className="space-y-6 max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
      <div className="flex justify-between items-center">
        <div>
          <h1 className="text-2xl font-bold text-[var(--text-primary)]">總資產儀表板</h1>
          <p className="text-[var(--text-muted)] mt-1">查看您的所有投資與銀行存款總覽</p>
        </div>
      </div>

      <CoreMetricsSection
        data={{
          investmentRatio: assetsData?.investmentRatio ?? 0,
          stockRatio: assetsData?.stockRatio ?? 0,
        }}
      />

      <AssetsBreakdownPieChart
        portfolioMarketValue={assetsData?.investmentTotal ?? 0}
        cashBalance={assetsData?.cashBalance ?? 0}
        disposableDeposit={assetsData?.disposableDeposit ?? 0}
        nonDisposableDeposit={assetsData?.nonDisposableDeposit ?? 0}
        isLoading={isLoading}
      />

      {allocationsError ? (
        <div className="p-3 rounded border border-red-500/40 bg-red-500/10 text-red-200 text-sm" role="alert">
          {allocationsError}
        </div>
      ) : null}

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <DisposableAssetsSection
          portfolioValue={assetsData?.portfolioValue ?? 0}
          cashBalance={assetsData?.cashBalance ?? 0}
          disposableDeposit={assetsData?.disposableDeposit ?? 0}
          investmentRatio={assetsData?.investmentRatio ?? 0}
          investmentTotal={assetsData?.investmentTotal ?? 0}
        />

        <NonDisposableAssetsSection
          nonDisposableDeposit={assetsData?.nonDisposableDeposit ?? 0}
          nonDisposableAllocations={nonDisposableAllocations}
        />
      </div>
    </div>
  );
}
