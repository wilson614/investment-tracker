/**
 * MarketContext
 *
 * 市場估值指標卡片：顯示各區域的 CAPE（Shiller P/E）、中位數與歷史百分位，並提供排序/篩選與顯示區域設定。
 */

import { useState, useMemo } from 'react';
import { Loader2, TrendingUp, TrendingDown, Minus, AlertCircle, Settings, X, Check, Search, Info } from 'lucide-react';
import { useCapeData } from '../../hooks/useCapeData';
import type { CapeDisplayItem, CapeValuation } from '../../types';

type CapeSortKey = 'default' | 'cape-asc' | 'cape-desc' | 'percentile-asc' | 'percentile-desc';

const CAPE_SORT_OPTIONS: { value: CapeSortKey; label: string }[] = [
  { value: 'default', label: '預設' },
  { value: 'cape-asc', label: 'CAPE ↑' },
  { value: 'cape-desc', label: 'CAPE ↓' },
  { value: 'percentile-asc', label: '百分位 ↑' },
  { value: 'percentile-desc', label: '百分位 ↓' },
];

interface ValuationBadgeProps {
  /** 估值分類（cheap/fair/expensive） */
  valuation: CapeValuation;
}

function ValuationBadge({ valuation }: ValuationBadgeProps) {
  const config: Record<CapeValuation, { label: string; className: string; icon: React.ReactNode }> = {
    cheap: {
      label: '便宜',
      className: 'bg-green-500/20 text-green-400 border-green-500/30',
      icon: <TrendingDown className="w-3 h-3" />,
    },
    fair: {
      label: '合理',
      className: 'bg-yellow-500/20 text-yellow-400 border-yellow-500/30',
      icon: <Minus className="w-3 h-3" />,
    },
    expensive: {
      label: '昂貴',
      className: 'bg-red-500/20 text-red-400 border-red-500/30',
      icon: <TrendingUp className="w-3 h-3" />,
    },
  };

  const { label, className, icon } = config[valuation];

  return (
    <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium border ${className}`}>
      {icon}
      {label}
    </span>
  );
}

interface PercentileBarProps {
  /** 0-1 的小數（例如 0.83 表示 83%） */
  percentile: number;
}

function PercentileBar({ percentile }: PercentileBarProps) {
  const pct = Math.round(percentile * 100);
  // 依百分位決定顏色：低 = 綠（便宜）、中 = 黃（合理）、高 = 紅（昂貴）。
  const getColor = (p: number) => {
    if (p < 25) return 'bg-green-500';
    if (p < 75) return 'bg-yellow-500';
    return 'bg-red-500';
  };

  return (
    <div className="flex items-center gap-2" title={`歷史百分位: ${pct}%`}>
      <div className="w-16 h-1.5 bg-[var(--bg-tertiary)] rounded-full overflow-hidden">
        <div
          className={`h-full ${getColor(pct)} transition-all`}
          style={{ width: `${pct}%` }}
        />
      </div>
      <span className="text-xs text-[var(--text-muted)] font-mono w-8">{pct}%</span>
    </div>
  );
}

interface CapeRowProps {
  /** 單列 CAPE 顯示資料 */
  item: CapeDisplayItem;
}

function CapeRow({ item }: CapeRowProps) {
  // 若有 adjusted CAPE（例如排除通膨/結構差異調整），則用調整後數值；否則使用原始 CAPE。
  const displayCape = item.adjustedCape ?? item.cape;
  const hasAdjusted = item.adjustedCape != null;

  return (
    <div className="flex items-center justify-between py-2.5 border-b border-[var(--border-color)] last:border-b-0">
      <div className="flex items-center gap-3 min-w-0">
        <span className="text-[var(--text-primary)] font-medium truncate">{item.region}</span>
        <ValuationBadge valuation={item.valuation} />
      </div>
      <div className="flex items-center gap-4 text-sm shrink-0">
        <div className="text-right">
          <div className="text-[var(--text-primary)] font-mono text-lg font-semibold" title={hasAdjusted ? `原始: ${item.cape.toFixed(1)}` : undefined}>
            {displayCape.toFixed(1)}
            {hasAdjusted && <span className="text-[var(--accent-peach)] text-xs ml-1">*</span>}
          </div>
        </div>
        <div className="text-right hidden sm:block">
          <div className="text-[var(--text-muted)] text-xs">中位數</div>
          <div className="text-[var(--text-secondary)] font-mono text-sm">{item.median.toFixed(1)}</div>
        </div>
        <div className="w-24">
          <PercentileBar percentile={item.percentile} />
        </div>
      </div>
    </div>
  );
}

interface MarketContextProps {
  /** 額外 className */
  className?: string;
}

export function MarketContext({ className = '' }: MarketContextProps) {
  const { data, dataDate, isLoading, error, selectedRegions, availableRegions, setSelectedRegions } = useCapeData();
  const [showSettings, setShowSettings] = useState(false);
  const [tempSelectedRegions, setTempSelectedRegions] = useState<string[]>(selectedRegions);
  const [initialSelectedRegions, setInitialSelectedRegions] = useState<string[]>(selectedRegions);
  const [searchQuery, setSearchQuery] = useState('');
  const [sortKey, setSortKey] = useState<CapeSortKey>('default');

  const sortedData = useMemo(() => {
    if (!data) return null;
    if (sortKey === 'default') return data;

    // 排序時一律以 adjusted CAPE 優先，確保顯示與排序一致。
    return [...data].sort((a, b) => {
      const aValue = a.adjustedCape ?? a.cape;
      const bValue = b.adjustedCape ?? b.cape;
      switch (sortKey) {
        case 'cape-asc': return aValue - bValue;
        case 'cape-desc': return bValue - aValue;
        case 'percentile-asc': return a.percentile - b.percentile;
        case 'percentile-desc': return b.percentile - a.percentile;
        default: return 0;
      }
    });
  }, [data, sortKey]);

  const filteredRegions = useMemo(() => {
    let regions = availableRegions;

    // 依搜尋字串過濾（key/label 任一包含即保留）。
    if (searchQuery.trim()) {
      const query = searchQuery.toLowerCase();
      regions = regions.filter(
        (region) =>
          region.key.toLowerCase().includes(query) ||
          region.label.toLowerCase().includes(query)
      );
    }

    // 排序：將「開啟設定時原本已選取」的區域先排到前面，方便使用者快速檢視目前選項。
    return [...regions].sort((a, b) => {
      const aSelected = initialSelectedRegions.includes(a.key);
      const bSelected = initialSelectedRegions.includes(b.key);
      if (aSelected && !bSelected) return -1;
      if (!aSelected && bSelected) return 1;
      return 0;
    });
  }, [availableRegions, searchQuery, initialSelectedRegions]);

  /**
   * 開啟設定：以目前已選取的 regions 初始化暫存狀態，並重置搜尋字串。
   */
  const handleOpenSettings = () => {
    setTempSelectedRegions(selectedRegions);
    setInitialSelectedRegions(selectedRegions);
    setSearchQuery('');
    setShowSettings(true);
  };

  /**
   * 儲存設定：只有在至少選取 1 個 region 時才會套用，避免整張卡片無資料。
   */
  const handleSaveSettings = () => {
    if (tempSelectedRegions.length > 0) {
      setSelectedRegions(tempSelectedRegions);
    }
    setShowSettings(false);
  };

  /**
   * 取消設定：放棄 temp 選擇，並關閉設定對話框。
   */
  const handleCancelSettings = () => {
    setTempSelectedRegions(selectedRegions);
    setShowSettings(false);
  };

  /**
   * 切換單一 region 的選取狀態（設定對話框用）。
   */
  const toggleRegion = (regionKey: string) => {
    setTempSelectedRegions((prev) => {
      if (prev.includes(regionKey)) {
        return prev.filter((r) => r !== regionKey);
      }
      return [...prev, regionKey];
    });
  };

  // 錯誤時以 placeholder 顯示，避免整個 Dashboard 崩潰。
  if (error) {
    return (
      <div className={`card-dark ${className}`}>
        <div className="px-5 py-4 border-b border-[var(--border-color)]">
          <h2 className="text-lg font-bold text-[var(--text-primary)]">市場估值指標</h2>
          <p className="text-xs text-[var(--text-muted)]">CAPE (Shiller P/E)</p>
        </div>
        <div className="p-5">
          <div className="flex items-center gap-3 text-[var(--text-muted)]">
            <AlertCircle className="w-5 h-5 text-yellow-500" />
            <div>
              <p className="text-sm">暫時無法取得 CAPE 資料</p>
              <p className="text-xs mt-1">請稍後重試或檢查網路連線</p>
            </div>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className={`card-dark ${className}`}>
      <div className="px-5 py-4 border-b border-[var(--border-color)] flex items-center justify-between">
        <div>
          <div className="flex items-center gap-1.5">
            <h2 className="text-lg font-bold text-[var(--text-primary)]">市場估值指標</h2>
            <div className="relative group">
              <Info className="w-4 h-4 text-[var(--text-muted)] cursor-help" />
              <div className="absolute left-0 top-full mt-1 hidden group-hover:block z-10">
                <div className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg p-2 shadow-lg text-xs text-[var(--text-secondary)] whitespace-nowrap">
                  <p>百分位條 = CAPE 歷史位置</p>
                  <p className="mt-1"><span className="text-[var(--accent-peach)]">*</span> = 已根據即時指數調整</p>
                </div>
              </div>
            </div>
          </div>
          <p className="text-xs text-[var(--text-muted)]">
            CAPE (Shiller P/E) {dataDate && `• 資料日期: ${dataDate}`}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <select
            value={sortKey}
            onChange={(e) => setSortKey(e.target.value as CapeSortKey)}
            className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded px-2 py-1.5 text-xs text-[var(--text-secondary)] focus:outline-none focus:border-[var(--accent-peach)] h-8"
          >
            {CAPE_SORT_OPTIONS.map((opt) => (
              <option key={opt.value} value={opt.value}>{opt.label}</option>
            ))}
          </select>
          <button
            onClick={handleOpenSettings}
            className="btn-dark p-2 h-8 flex items-center justify-center"
            title="選擇市場"
          >
            <Settings className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* Settings Modal */}
      {showSettings && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="card-dark w-full max-w-md mx-4">
            <div className="px-5 py-4 border-b border-[var(--border-color)] flex items-center justify-between">
              <h3 className="text-lg font-bold text-[var(--text-primary)]">選擇顯示市場</h3>
              <button onClick={handleCancelSettings} className="text-[var(--text-muted)] hover:text-[var(--text-primary)]">
                <X className="w-5 h-5" />
              </button>
            </div>
            <div className="px-5 pt-4">
              <div className="relative">
                <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-[var(--text-muted)]" />
                <input
                  type="text"
                  placeholder="搜尋市場..."
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  className="w-full pl-10 pr-4 py-2 bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg text-[var(--text-primary)] placeholder:text-[var(--text-muted)] focus:outline-none focus:border-[var(--accent-peach)]"
                />
              </div>
            </div>
            <div className="p-5 max-h-[50vh] overflow-y-auto">
              <div className="grid grid-cols-2 gap-2">
                {filteredRegions.map((region) => (
                  <button
                    key={region.key}
                    onClick={() => toggleRegion(region.key)}
                    className={`flex items-center gap-2 px-3 py-2 rounded-lg border transition-colors ${
                      tempSelectedRegions.includes(region.key)
                        ? 'border-[var(--accent-peach)] bg-[var(--accent-peach)]/10 text-[var(--text-primary)]'
                        : 'border-[var(--border-color)] text-[var(--text-muted)] hover:border-[var(--text-muted)]'
                    }`}
                  >
                    <div className={`w-4 h-4 rounded border flex items-center justify-center ${
                      tempSelectedRegions.includes(region.key)
                        ? 'bg-[var(--accent-peach)] border-[var(--accent-peach)]'
                        : 'border-[var(--text-muted)]'
                    }`}>
                      {tempSelectedRegions.includes(region.key) && (
                        <Check className="w-3 h-3 text-[var(--bg-primary)]" />
                      )}
                    </div>
                    <span className="text-sm truncate">{region.label}</span>
                  </button>
                ))}
              </div>
              {filteredRegions.length === 0 && (
                <p className="text-center text-[var(--text-muted)] py-4">
                  找不到符合的市場
                </p>
              )}
            </div>
            <div className="px-5 py-4 border-t border-[var(--border-color)] flex justify-end gap-3">
              <button
                onClick={handleCancelSettings}
                className="btn-dark px-4 py-2"
              >
                取消
              </button>
              <button
                onClick={handleSaveSettings}
                className="btn-accent px-4 py-2"
              >
                儲存
              </button>
            </div>
          </div>
        </div>
      )}

      <div className="p-5">
        {isLoading && !data ? (
          <div className="flex items-center justify-center py-8">
            <Loader2 className="w-6 h-6 animate-spin text-[var(--text-muted)]" />
          </div>
        ) : sortedData && sortedData.length > 0 ? (
          <div>
            <div className="max-h-[240px] overflow-y-auto">
              {sortedData.map((item) => (
                <CapeRow key={item.region} item={item} />
              ))}
            </div>
          </div>
        ) : (
          <div className="text-center py-8 text-[var(--text-muted)]">
            無可用資料
          </div>
        )}
      </div>
    </div>
  );
}
