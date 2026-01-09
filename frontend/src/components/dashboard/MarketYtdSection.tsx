/**
 * MarketYtdSection Component
 * Displays YTD (Year-to-Date) returns for benchmark ETFs
 */

import { useState, useMemo } from 'react';
import { Loader2, Info, Settings, X, Check } from 'lucide-react';
import { useMarketYtdData } from '../../hooks/useMarketYtdData';
import type { MarketYtdReturn } from '../../types';

type YtdSortKey = 'ytd-desc' | 'ytd-asc' | 'name-asc' | 'name-desc';

const YTD_SORT_OPTIONS: { value: YtdSortKey; label: string }[] = [
  { value: 'ytd-desc', label: 'YTD ↓' },
  { value: 'ytd-asc', label: 'YTD ↑' },
  { value: 'name-asc', label: '名稱 A-Z' },
  { value: 'name-desc', label: '名稱 Z-A' },
];

const YTD_PREFS_KEY = 'ytd_benchmark_preferences';
const DEFAULT_BENCHMARKS = [
  'All Country', 'US Large', 'Developed Markets Large',
  'Dev ex US Large', 'Emerging Markets', 'Europe', 'Japan', 'China', 'Taiwan 0050'
];

function getSelectedBenchmarks(): string[] {
  try {
    const stored = localStorage.getItem(YTD_PREFS_KEY);
    if (stored) {
      const benchmarks = JSON.parse(stored);
      if (Array.isArray(benchmarks) && benchmarks.length > 0) {
        return benchmarks;
      }
    }
  } catch {
    // Ignore
  }
  return DEFAULT_BENCHMARKS;
}

function saveSelectedBenchmarks(benchmarks: string[]): void {
  try {
    localStorage.setItem(YTD_PREFS_KEY, JSON.stringify(benchmarks));
  } catch {
    // Ignore
  }
}

interface YtdCardProps {
  item: MarketYtdReturn;
}

function YtdCard({ item }: YtdCardProps) {
  const hasYtd = item.ytdReturnPercent != null;
  const isPositive = hasYtd && item.ytdReturnPercent! >= 0;

  return (
    <div className="bg-[var(--bg-tertiary)] rounded-lg px-2 py-4 text-center">
      <div className="text-sm text-[var(--text-primary)] truncate mb-1" title={item.marketKey}>
        {item.marketKey}
      </div>
      {item.error ? (
        <div className="text-base text-[var(--color-warning)] my-1">N/A</div>
      ) : hasYtd ? (
        <div className={`text-xl font-bold font-mono my-1 ${isPositive ? 'text-green-400' : 'text-red-400'}`}>
          {isPositive ? '+' : ''}{item.ytdReturnPercent!.toFixed(1)}%
        </div>
      ) : (
        <div className="text-base text-[var(--text-muted)] my-1">--</div>
      )}
      <div className="font-mono text-xs text-[var(--text-muted)]">{item.symbol}</div>
    </div>
  );
}

interface MarketYtdSectionProps {
  className?: string;
}

export function MarketYtdSection({ className = '' }: MarketYtdSectionProps) {
  const { data, isLoading } = useMarketYtdData();
  const [sortKey, setSortKey] = useState<YtdSortKey>('ytd-desc');
  const [selectedBenchmarks, setSelectedBenchmarksState] = useState<string[]>(getSelectedBenchmarks);
  const [showSettings, setShowSettings] = useState(false);
  const [tempSelected, setTempSelected] = useState<string[]>(selectedBenchmarks);

  const availableBenchmarks = useMemo(() => {
    if (!data?.benchmarks) return [];
    return data.benchmarks.map(b => b.marketKey);
  }, [data?.benchmarks]);

  const filteredAndSorted = useMemo(() => {
    if (!data?.benchmarks) return null;

    // Filter by selected benchmarks
    let filtered = data.benchmarks.filter(b => selectedBenchmarks.includes(b.marketKey));

    // Sort
    return [...filtered].sort((a, b) => {
      switch (sortKey) {
        case 'ytd-asc':
          return (a.ytdReturnPercent ?? -Infinity) - (b.ytdReturnPercent ?? -Infinity);
        case 'ytd-desc':
          return (b.ytdReturnPercent ?? -Infinity) - (a.ytdReturnPercent ?? -Infinity);
        case 'name-asc':
          return a.marketKey.localeCompare(b.marketKey);
        case 'name-desc':
          return b.marketKey.localeCompare(a.marketKey);
        default:
          return 0;
      }
    });
  }, [data?.benchmarks, selectedBenchmarks, sortKey]);

  const handleOpenSettings = () => {
    setTempSelected(selectedBenchmarks);
    setShowSettings(true);
  };

  const handleSaveSettings = () => {
    if (tempSelected.length > 0) {
      setSelectedBenchmarksState(tempSelected);
      saveSelectedBenchmarks(tempSelected);
    }
    setShowSettings(false);
  };

  const toggleBenchmark = (key: string) => {
    setTempSelected(prev => {
      if (prev.includes(key)) {
        if (prev.length === 1) return prev;
        return prev.filter(k => k !== key);
      }
      return [...prev, key];
    });
  };

  return (
    <div className={`card-dark ${className}`}>
      <div className="px-5 py-4 border-b border-[var(--border-color)] flex items-center justify-between">
        <div className="flex items-center gap-2">
          <div>
            <h2 className="text-lg font-bold text-[var(--text-primary)]">年初至今報酬</h2>
            <p className="text-xs text-[var(--text-muted)]">
              {data?.year ? `${data.year} 年度績效 (YTD)` : '基準 ETF 年度績效'}
            </p>
          </div>
          <div className="relative group">
            <Info className="w-4 h-4 text-[var(--text-muted)] cursor-help" />
            <div className="absolute left-0 bottom-full mb-2 hidden group-hover:block z-10">
              <div className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg p-2 shadow-lg text-xs text-[var(--text-secondary)] whitespace-nowrap">
                報酬率 = (現價 - 年初價格) / 年初價格 × 100
              </div>
            </div>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <select
            value={sortKey}
            onChange={(e) => setSortKey(e.target.value as YtdSortKey)}
            className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded px-2 py-1.5 text-xs text-[var(--text-secondary)] focus:outline-none focus:border-[var(--accent-peach)] h-8"
          >
            {YTD_SORT_OPTIONS.map((opt) => (
              <option key={opt.value} value={opt.value}>{opt.label}</option>
            ))}
          </select>
          <button onClick={handleOpenSettings} className="btn-dark p-2 h-8 flex items-center justify-center" title="選擇基準">
            <Settings className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* Settings Modal */}
      {showSettings && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="card-dark w-full max-w-md mx-4">
            <div className="px-5 py-4 border-b border-[var(--border-color)] flex items-center justify-between">
              <h3 className="text-lg font-bold text-[var(--text-primary)]">選擇顯示基準</h3>
              <button onClick={() => setShowSettings(false)} className="text-[var(--text-muted)] hover:text-[var(--text-primary)]">
                <X className="w-5 h-5" />
              </button>
            </div>
            <div className="p-5 max-h-[50vh] overflow-y-auto">
              <div className="grid grid-cols-2 gap-2">
                {availableBenchmarks.map((key) => (
                  <button
                    key={key}
                    onClick={() => toggleBenchmark(key)}
                    className={`flex items-center gap-2 px-3 py-2 rounded-lg border transition-colors text-left ${
                      tempSelected.includes(key)
                        ? 'border-[var(--accent-peach)] bg-[var(--accent-peach)]/10 text-[var(--text-primary)]'
                        : 'border-[var(--border-color)] text-[var(--text-muted)] hover:border-[var(--text-muted)]'
                    }`}
                  >
                    <div className={`w-4 h-4 rounded border flex items-center justify-center shrink-0 ${
                      tempSelected.includes(key)
                        ? 'bg-[var(--accent-peach)] border-[var(--accent-peach)]'
                        : 'border-[var(--text-muted)]'
                    }`}>
                      {tempSelected.includes(key) && <Check className="w-3 h-3 text-[var(--bg-primary)]" />}
                    </div>
                    <span className="text-sm truncate">{key}</span>
                  </button>
                ))}
              </div>
            </div>
            <div className="px-5 py-4 border-t border-[var(--border-color)] flex justify-end gap-3">
              <button onClick={() => setShowSettings(false)} className="btn-dark px-4 py-2">取消</button>
              <button onClick={handleSaveSettings} className="btn-accent px-4 py-2">儲存</button>
            </div>
          </div>
        </div>
      )}

      <div className="p-5">
        {isLoading && !data ? (
          <div className="flex items-center justify-center py-8">
            <Loader2 className="w-6 h-6 animate-spin text-[var(--text-muted)]" />
          </div>
        ) : filteredAndSorted && filteredAndSorted.length > 0 ? (
          <div className="grid grid-cols-5 gap-2">
            {filteredAndSorted.map((item) => (
              <YtdCard key={item.marketKey} item={item} />
            ))}
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
