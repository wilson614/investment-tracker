/**
 * BenchmarkSettings Component
 *
 * 讓使用者管理自訂基準標的（UserBenchmark）。
 * 可新增任意 ticker/market 組合，用於 Performance 頁面的績效比較。
 */
import { useState, useEffect } from 'react';
import { Plus, Trash2, Loader2, AlertCircle } from 'lucide-react';
import { userBenchmarkApi, etfClassificationApi } from '../../services/api';
import { StockMarket as StockMarketEnum } from '../../types';
import type { UserBenchmark, StockMarket, CreateUserBenchmarkRequest } from '../../types';

const MARKET_LABELS: Record<StockMarket, string> = {
  [StockMarketEnum.TW]: '台股',
  [StockMarketEnum.US]: '美股',
  [StockMarketEnum.UK]: '英股',
  [StockMarketEnum.EU]: '歐股',
};

/**
 * 依股票代號自動判斷市場
 */
function guessMarketFromTicker(ticker: string): StockMarket {
  if (!ticker) return StockMarketEnum.US;
  const trimmed = ticker.trim().toUpperCase();
  // 台股：數字開頭
  if (/^\d/.test(trimmed)) return StockMarketEnum.TW;
  // 英股：.L 結尾
  if (trimmed.endsWith('.L')) return StockMarketEnum.UK;
  return StockMarketEnum.US;
}

interface BenchmarkSettingsProps {
  onUpdate?: () => void;
}

export function BenchmarkSettings({ onUpdate }: BenchmarkSettingsProps) {
  const [benchmarks, setBenchmarks] = useState<UserBenchmark[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Add form state
  const [newTicker, setNewTicker] = useState('');
  const [newMarket, setNewMarket] = useState<StockMarket>(StockMarketEnum.US);
  const [newDisplayName, setNewDisplayName] = useState('');
  const [isAdding, setIsAdding] = useState(false);
  const [addError, setAddError] = useState<string | null>(null);

  // ETF type warnings
  const [etfWarnings, setEtfWarnings] = useState<Record<string, string>>({});

  // Fetch benchmarks on mount
  useEffect(() => {
    loadBenchmarks();
  }, []);

  const loadBenchmarks = async () => {
    setIsLoading(true);
    setError(null);
    try {
      const data = await userBenchmarkApi.getAll();
      setBenchmarks(data);
      // Check ETF types for warnings
      checkEtfTypes(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : '載入失敗');
    } finally {
      setIsLoading(false);
    }
  };

  const checkEtfTypes = async (items: UserBenchmark[]) => {
    const warnings: Record<string, string> = {};
    await Promise.all(
      items.map(async (b) => {
        try {
          const result = await etfClassificationApi.getClassification(b.ticker);
          if (result.type === 'Distributing') {
            warnings[b.id] = '配息型 ETF，年報酬不含股息';
          }
        } catch {
          // Ignore - not an ETF or classification unknown
        }
      })
    );
    setEtfWarnings(warnings);
  };

  const handleTickerChange = (value: string) => {
    setNewTicker(value.toUpperCase());
    // Auto-detect market
    setNewMarket(guessMarketFromTicker(value));
  };

  const handleAdd = async () => {
    if (!newTicker.trim()) {
      setAddError('請輸入股票代號');
      return;
    }

    setIsAdding(true);
    setAddError(null);

    try {
      const request: CreateUserBenchmarkRequest = {
        ticker: newTicker.trim().toUpperCase(),
        market: newMarket,
        displayName: newDisplayName.trim() || undefined,
      };
      await userBenchmarkApi.create(request);
      // Reload list
      await loadBenchmarks();
      // Reset form
      setNewTicker('');
      setNewMarket(StockMarketEnum.US);
      setNewDisplayName('');
      onUpdate?.();
    } catch (err) {
      setAddError(err instanceof Error ? err.message : '新增失敗');
    } finally {
      setIsAdding(false);
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await userBenchmarkApi.delete(id);
      setBenchmarks(benchmarks.filter((b) => b.id !== id));
      onUpdate?.();
    } catch (err) {
      setError(err instanceof Error ? err.message : '刪除失敗');
    }
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-8">
        <Loader2 className="w-6 h-6 animate-spin text-[var(--accent-peach)]" />
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {error && (
        <div className="p-3 rounded-lg bg-[var(--color-danger)]/10 border border-[var(--color-danger)]/30">
          <p className="text-sm text-[var(--color-danger)]">{error}</p>
        </div>
      )}

      {/* Add Form */}
      <div className="p-4 rounded-lg bg-[var(--bg-secondary)] border border-[var(--border-color)]">
        <h4 className="text-sm font-medium text-[var(--text-primary)] mb-3">新增自訂基準</h4>
        <div className="grid grid-cols-1 sm:grid-cols-4 gap-3">
          <div>
            <label className="block text-xs text-[var(--text-muted)] mb-1">股票代號</label>
            <input
              type="text"
              value={newTicker}
              onChange={(e) => handleTickerChange(e.target.value)}
              placeholder="如 VWRA.L"
              className="input-dark w-full"
              disabled={isAdding}
            />
          </div>
          <div>
            <label className="block text-xs text-[var(--text-muted)] mb-1">市場</label>
            <select
              value={newMarket}
              onChange={(e) => setNewMarket(Number(e.target.value) as StockMarket)}
              className="input-dark w-full"
              disabled={isAdding}
            >
              {Object.entries(MARKET_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="block text-xs text-[var(--text-muted)] mb-1">顯示名稱（選填）</label>
            <input
              type="text"
              value={newDisplayName}
              onChange={(e) => setNewDisplayName(e.target.value)}
              placeholder="如 全球股票"
              className="input-dark w-full"
              disabled={isAdding}
            />
          </div>
          <div className="flex items-end">
            <button
              type="button"
              onClick={handleAdd}
              disabled={isAdding || !newTicker.trim()}
              className="btn-accent w-full py-2 flex items-center justify-center gap-2"
            >
              {isAdding ? (
                <Loader2 className="w-4 h-4 animate-spin" />
              ) : (
                <Plus className="w-4 h-4" />
              )}
              新增
            </button>
          </div>
        </div>
        {addError && (
          <p className="text-sm text-[var(--color-danger)] mt-2">{addError}</p>
        )}
      </div>

      {/* Benchmark List */}
      {benchmarks.length === 0 ? (
        <p className="text-sm text-[var(--text-muted)] text-center py-4">
          尚未新增自訂基準
        </p>
      ) : (
        <div className="divide-y divide-[var(--border-color)]">
          {benchmarks.map((b) => (
            <div
              key={b.id}
              className="py-3 flex items-center justify-between"
            >
              <div className="flex items-center gap-3">
                <span className="font-medium text-[var(--text-primary)]">
                  {b.ticker}
                </span>
                <span className="text-xs px-2 py-0.5 rounded bg-[var(--bg-tertiary)] text-[var(--text-muted)]">
                  {MARKET_LABELS[b.market]}
                </span>
                {b.displayName && (
                  <span className="text-sm text-[var(--text-secondary)]">
                    {b.displayName}
                  </span>
                )}
                {etfWarnings[b.id] && (
                  <span className="flex items-center gap-1 text-xs text-[var(--color-warning)]">
                    <AlertCircle className="w-3 h-3" />
                    {etfWarnings[b.id]}
                  </span>
                )}
              </div>
              <button
                type="button"
                onClick={() => handleDelete(b.id)}
                className="p-2 text-[var(--text-muted)] hover:text-[var(--color-danger)] transition-colors"
                title="刪除"
              >
                <Trash2 className="w-4 h-4" />
              </button>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export default BenchmarkSettings;
