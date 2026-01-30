import { TotalAssetsBanner } from '../components/TotalAssetsBanner';
import { AssetCategorySummary } from '../components/AssetCategorySummary';
import { AssetsBreakdownPieChart } from '../components/AssetsBreakdownPieChart';
import { useTotalAssets } from '../hooks/useTotalAssets';

export function TotalAssetsDashboard() {
  const { summary: assetsData, isLoading } = useTotalAssets();

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
    </div>
  );
}
