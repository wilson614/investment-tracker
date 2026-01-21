/**
 * Settings Page
 *
 * 帳戶設定頁：提供自訂基準指數、股票分割等資料設定。
 * 個人資料和密碼變更已移至導覽列的下拉選單。
 */
import { useNavigate } from 'react-router-dom';
import { ArrowLeft, TrendingUp, Scissors } from 'lucide-react';
import { BenchmarkSettings } from '../components/settings/BenchmarkSettings';
import { StockSplitSettings } from '../components/settings/StockSplitSettings';

export default function Settings() {
  const navigate = useNavigate();

  return (
    <div className="min-h-screen py-8">
      <div className="max-w-2xl mx-auto px-4 sm:px-6 lg:px-8">
        {/* Back Button */}
        <button
          onClick={() => navigate(-1)}
          className="flex items-center gap-2 text-[var(--text-secondary)] hover:text-[var(--text-primary)] mb-6 text-base transition-colors"
        >
          <ArrowLeft className="w-5 h-5" />
          返回
        </button>

        <h1 className="text-2xl font-bold text-[var(--text-primary)] mb-8">設定</h1>

        {/* Benchmark Settings Section */}
        <div className="card-dark p-6 mb-6">
          <h2 className="text-lg font-bold text-[var(--text-primary)] mb-4 flex items-center gap-2">
            <TrendingUp className="w-5 h-5 text-[var(--accent-peach)]" />
            自訂基準指數
          </h2>
          <p className="text-sm text-[var(--text-muted)] mb-4">
            新增自訂股票/ETF 作為績效比較基準。這些會顯示在「歷史績效」頁面的績效比較圖表中。
          </p>
          <BenchmarkSettings />
        </div>

        {/* Stock Split Settings Section */}
        <div className="card-dark p-6">
          <h2 className="text-lg font-bold text-[var(--text-primary)] mb-4 flex items-center gap-2">
            <Scissors className="w-5 h-5 text-[var(--accent-peach)]" />
            股票分割
          </h2>
          <p className="text-sm text-[var(--text-muted)] mb-4">
            記錄股票分割（拆股/合股）事件。系統會自動調整歷史交易的股數與成本計算。
          </p>
          <StockSplitSettings />
        </div>
      </div>
    </div>
  );
}
