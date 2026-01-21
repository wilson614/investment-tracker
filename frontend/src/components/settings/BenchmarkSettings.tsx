/**
 * BenchmarkSettings Component
 *
 * 讓使用者管理自訂基準標的（UserBenchmark）。
 * 可新增任意 ticker/market 組合，用於 Performance 頁面的績效比較。
 */
import { useState, useEffect, useCallback } from 'react';
import { Plus, Trash2, Loader2, AlertCircle } from 'lucide-react';
import { userBenchmarkApi, etfClassificationApi, stockPriceApi } from '../../services/api';
import { StockMarket as StockMarketEnum } from '../../types';
import type { UserBenchmark, StockMarket, CreateUserBenchmarkRequest } from '../../types';
import { isEuronextSymbol } from '../../constants';

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
  const [isAdding, setIsAdding] = useState(false);
  const [addError, setAddError] = useState<string | null>(null);
  const [isDetectingMarket, setIsDetectingMarket] = useState(false);

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

  /**
   * 自動偵測市場：先嘗試 US，找不到再嘗試 UK。
   */
  const detectMarket = useCallback(async (ticker: string): Promise<StockMarket | null> => {
    if (!ticker.trim()) return null;
    const trimmed = ticker.trim().toUpperCase();

    // 台股：數字開頭
    if (/^\d/.test(trimmed)) return StockMarketEnum.TW;

    // .L 結尾直接判定為英股
    if (trimmed.endsWith('.L')) return StockMarketEnum.UK;

    // Euronext 股票
    if (isEuronextSymbol(trimmed)) return StockMarketEnum.EU;

    // 對於其他 ticker，先嘗試查詢 US 市場
    try {
      const usQuote = await stockPriceApi.getQuote(StockMarketEnum.US, trimmed);
      if (usQuote) return StockMarketEnum.US;
    } catch {
      // US 查詢失敗
    }

    // 嘗試 UK 市場
    try {
      const ukQuote = await stockPriceApi.getQuote(StockMarketEnum.UK, trimmed);
      if (ukQuote) return StockMarketEnum.UK;
    } catch {
      // UK 也找不到
    }

    return StockMarketEnum.US;
  }, []);

  const handleTickerChange = (value: string) => {
    setNewTicker(value.toUpperCase());
    // Just set initial guess, real detection happens on blur
    setNewMarket(guessMarketFromTicker(value));
  };

  const handleTickerBlur = async () => {
    const ticker = newTicker.trim();
    if (!ticker) return;

    // Skip detection for Taiwan stocks
    if (/^\d/.test(ticker)) return;

    setIsDetectingMarket(true);
    try {
      const detectedMarket = await detectMarket(ticker);
      if (detectedMarket !== null) {
        setNewMarket(detectedMarket);
      }
    } finally {
      setIsDetectingMarket(false);
    }
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
      };
      await userBenchmarkApi.create(request);
      // Reload list
      await loadBenchmarks();
      // Reset form
      setNewTicker('');
      setNewMarket(StockMarketEnum.US);
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
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
          <div>
            <label className="block text-xs text-[var(--text-muted)] mb-1">股票代號</label>
            <input
              type="text"
              value={newTicker}
              onChange={(e) => handleTickerChange(e.target.value)}
              onBlur={handleTickerBlur}
              placeholder="如 VWRA.L"
              className="input-dark w-full"
              disabled={isAdding}
            />
          </div>
          <div>
            <label className="block text-xs text-[var(--text-muted)] mb-1">
              市場
              {isDetectingMarket && <span className="ml-1 text-[var(--text-muted)]">偵測中...</span>}
            </label>
            <select
              value={newMarket}
              onChange={(e) => setNewMarket(Number(e.target.value) as StockMarket)}
              className="input-dark w-full"
              disabled={isAdding || isDetectingMarket}
            >
              {Object.entries(MARKET_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
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
