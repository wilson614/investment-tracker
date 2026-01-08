/**
 * MarketContext Component
 * Displays Global CAPE (Cyclically Adjusted P/E) data with valuation context
 */

import { useState, useMemo } from 'react';
import { RefreshCw, Loader2, TrendingUp, TrendingDown, Minus, AlertCircle, Settings, X, Check, Search } from 'lucide-react';
import { useCapeData } from '../../hooks/useCapeData';
import type { CapeDisplayItem, CapeValuation } from '../../types';

interface ValuationBadgeProps {
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
  percentile: number;
}

function PercentileBar({ percentile }: PercentileBarProps) {
  const pct = Math.round(percentile * 100);
  // Color based on percentile: low = green, mid = yellow, high = red
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
  item: CapeDisplayItem;
}

function CapeRow({ item }: CapeRowProps) {
  return (
    <div className="flex items-center justify-between py-2.5 border-b border-[var(--border-color)] last:border-b-0">
      <div className="flex items-center gap-3 min-w-0">
        <span className="text-[var(--text-primary)] font-medium truncate">{item.region}</span>
        <ValuationBadge valuation={item.valuation} />
      </div>
      <div className="flex items-center gap-4 text-sm shrink-0">
        <div className="text-right">
          <div className="text-[var(--text-primary)] font-mono text-lg font-semibold">{item.cape.toFixed(1)}</div>
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
  className?: string;
}

export function MarketContext({ className = '' }: MarketContextProps) {
  const { data, dataDate, isLoading, error, selectedRegions, availableRegions, setSelectedRegions, refresh } = useCapeData();
  const [showSettings, setShowSettings] = useState(false);
  const [tempSelectedRegions, setTempSelectedRegions] = useState<string[]>(selectedRegions);
  const [searchQuery, setSearchQuery] = useState('');

  const filteredRegions = useMemo(() => {
    if (!searchQuery.trim()) return availableRegions;
    const query = searchQuery.toLowerCase();
    return availableRegions.filter(
      (region) =>
        region.key.toLowerCase().includes(query) ||
        region.label.toLowerCase().includes(query)
    );
  }, [availableRegions, searchQuery]);

  const handleOpenSettings = () => {
    setTempSelectedRegions(selectedRegions);
    setSearchQuery('');
    setShowSettings(true);
  };

  const handleSaveSettings = () => {
    if (tempSelectedRegions.length > 0) {
      setSelectedRegions(tempSelectedRegions);
    }
    setShowSettings(false);
  };

  const handleCancelSettings = () => {
    setTempSelectedRegions(selectedRegions);
    setShowSettings(false);
  };

  const toggleRegion = (regionKey: string) => {
    setTempSelectedRegions((prev) => {
      if (prev.includes(regionKey)) {
        // Don't allow removing the last region
        if (prev.length === 1) return prev;
        return prev.filter((r) => r !== regionKey);
      }
      return [...prev, regionKey];
    });
  };

  // Graceful degradation: show placeholder when data unavailable
  if (error) {
    return (
      <div className={`card-dark ${className}`}>
        <div className="px-5 py-4 border-b border-[var(--border-color)] flex items-center justify-between">
          <div>
            <h2 className="text-lg font-bold text-[var(--text-primary)]">市場估值指標</h2>
            <p className="text-xs text-[var(--text-muted)]">CAPE (Shiller P/E)</p>
          </div>
          <button
            onClick={refresh}
            className="btn-dark p-2"
            title="重新整理"
          >
            <RefreshCw className="w-4 h-4" />
          </button>
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
          <h2 className="text-lg font-bold text-[var(--text-primary)]">市場估值指標</h2>
          <p className="text-xs text-[var(--text-muted)]">
            CAPE (Shiller P/E) {dataDate && `• 資料日期: ${dataDate}`}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={handleOpenSettings}
            className="btn-dark p-2"
            title="選擇市場"
          >
            <Settings className="w-4 h-4" />
          </button>
          <button
            onClick={refresh}
            disabled={isLoading}
            className="btn-dark p-2 disabled:opacity-50"
            title="重新整理"
          >
            {isLoading ? (
              <Loader2 className="w-4 h-4 animate-spin" />
            ) : (
              <RefreshCw className="w-4 h-4" />
            )}
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
              <p className="text-xs text-[var(--text-muted)] mt-4">
                至少選擇一個市場
              </p>
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
        ) : data && data.length > 0 ? (
          <div>
            {data.map((item) => (
              <CapeRow key={item.region} item={item} />
            ))}
            <p className="text-xs text-[var(--text-muted)] mt-4">
              百分位條顯示目前 CAPE 在該市場歷史數據中的位置
            </p>
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
