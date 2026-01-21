/**
 * StockSplitSettings Component
 *
 * 讓使用者管理全域股票分割（StockSplit）資料。
 * 可新增、編輯、刪除股票分割，系統會自動調整歷史交易的股數與價格。
 */
import { useState, useEffect } from 'react';
import { Plus, Trash2, Loader2, Edit2, X, Check } from 'lucide-react';
import { stockSplitApi } from '../../services/api';
import { StockMarket as StockMarketEnum } from '../../types';
import type { StockSplit, StockMarket, CreateStockSplitRequest } from '../../types';

const MARKET_LABELS: Record<StockMarket, string> = {
  [StockMarketEnum.TW]: '台股',
  [StockMarketEnum.US]: '美股',
  [StockMarketEnum.UK]: '英股',
  [StockMarketEnum.EU]: '歐股',
};

/**
 * 依股票代號自動判斷市場
 */
function guessMarketFromSymbol(symbol: string): StockMarket {
  if (!symbol) return StockMarketEnum.US;
  const trimmed = symbol.trim().toUpperCase();
  // 台股：數字開頭
  if (/^\d/.test(trimmed)) return StockMarketEnum.TW;
  // 英股：.L 結尾
  if (trimmed.endsWith('.L')) return StockMarketEnum.UK;
  return StockMarketEnum.US;
}

/**
 * 格式化日期為 YYYY-MM-DD
 */
function formatDate(dateStr: string): string {
  const date = new Date(dateStr);
  return date.toISOString().split('T')[0];
}

interface StockSplitSettingsProps {
  onUpdate?: () => void;
}

export function StockSplitSettings({ onUpdate }: StockSplitSettingsProps) {
  const [splits, setSplits] = useState<StockSplit[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Add form state
  const [newSymbol, setNewSymbol] = useState('');
  const [newMarket, setNewMarket] = useState<StockMarket>(StockMarketEnum.US);
  const [newSplitDate, setNewSplitDate] = useState('');
  const [newFromShares, setNewFromShares] = useState('1');
  const [newToShares, setNewToShares] = useState('');
  const [isAdding, setIsAdding] = useState(false);
  const [addError, setAddError] = useState<string | null>(null);

  // Edit state
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editSplitDate, setEditSplitDate] = useState('');
  const [editFromShares, setEditFromShares] = useState('1');
  const [editToShares, setEditToShares] = useState('');
  const [isUpdating, setIsUpdating] = useState(false);

  // Fetch splits on mount
  useEffect(() => {
    loadSplits();
  }, []);

  const loadSplits = async () => {
    setIsLoading(true);
    setError(null);
    try {
      const data = await stockSplitApi.getAll();
      // 依日期由新到舊排序
      const sorted = [...data].sort(
        (a, b) => new Date(b.splitDate).getTime() - new Date(a.splitDate).getTime()
      );
      setSplits(sorted);
    } catch (err) {
      setError(err instanceof Error ? err.message : '載入失敗');
    } finally {
      setIsLoading(false);
    }
  };

  const handleSymbolChange = (value: string) => {
    setNewSymbol(value.toUpperCase());
    // Auto-detect market
    setNewMarket(guessMarketFromSymbol(value));
  };

  const handleAdd = async () => {
    if (!newSymbol.trim()) {
      setAddError('請輸入股票代號');
      return;
    }
    if (!newSplitDate) {
      setAddError('請輸入生效日期');
      return;
    }
    const fromShares = parseFloat(newFromShares);
    const toShares = parseFloat(newToShares);
    if (isNaN(fromShares) || fromShares <= 0 || isNaN(toShares) || toShares <= 0) {
      setAddError('請輸入有效的股數');
      return;
    }
    const ratio = toShares / fromShares;

    setIsAdding(true);
    setAddError(null);

    try {
      const request: CreateStockSplitRequest = {
        symbol: newSymbol.trim().toUpperCase(),
        market: newMarket,
        splitDate: new Date(newSplitDate).toISOString(),
        splitRatio: ratio,
      };
      await stockSplitApi.create(request);
      // Reload list
      await loadSplits();
      // Reset form
      setNewSymbol('');
      setNewMarket(StockMarketEnum.US);
      setNewSplitDate('');
      setNewFromShares('1');
      setNewToShares('');
      onUpdate?.();
    } catch (err) {
      setAddError(err instanceof Error ? err.message : '新增失敗');
    } finally {
      setIsAdding(false);
    }
  };

  const startEdit = (split: StockSplit) => {
    setEditingId(split.id);
    setEditSplitDate(formatDate(split.splitDate));
    // Convert ratio back to from/to format (assume from=1)
    setEditFromShares('1');
    setEditToShares(split.splitRatio.toString());
  };

  const cancelEdit = () => {
    setEditingId(null);
    setEditSplitDate('');
    setEditFromShares('1');
    setEditToShares('');
  };

  const handleUpdate = async (id: string) => {
    const fromShares = parseFloat(editFromShares);
    const toShares = parseFloat(editToShares);
    if (isNaN(fromShares) || fromShares <= 0 || isNaN(toShares) || toShares <= 0) {
      setError('請輸入有效的股數');
      return;
    }
    const ratio = toShares / fromShares;

    setIsUpdating(true);
    setError(null);

    try {
      await stockSplitApi.update(id, {
        splitDate: new Date(editSplitDate).toISOString(),
        splitRatio: ratio,
      });
      await loadSplits();
      cancelEdit();
      onUpdate?.();
    } catch (err) {
      setError(err instanceof Error ? err.message : '更新失敗');
    } finally {
      setIsUpdating(false);
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await stockSplitApi.delete(id);
      setSplits(splits.filter((s) => s.id !== id));
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
        <h4 className="text-sm font-medium text-[var(--text-primary)] mb-3">新增股票分割</h4>
        <div className="grid grid-cols-1 sm:grid-cols-5 gap-3">
          <div>
            <label className="block text-xs text-[var(--text-muted)] mb-1">股票代號</label>
            <input
              type="text"
              value={newSymbol}
              onChange={(e) => handleSymbolChange(e.target.value)}
              placeholder="如 0050"
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
            <label className="block text-xs text-[var(--text-muted)] mb-1">生效日期</label>
            <input
              type="date"
              value={newSplitDate}
              onChange={(e) => setNewSplitDate(e.target.value)}
              className="input-dark w-full"
              disabled={isAdding}
            />
          </div>
          <div>
            <label className="block text-xs text-[var(--text-muted)] mb-1">分割比例</label>
            <div className="flex items-center gap-1">
              <input
                type="number"
                step="1"
                min="1"
                value={newFromShares}
                onChange={(e) => setNewFromShares(e.target.value)}
                className="input-dark w-12 text-center"
                disabled={isAdding}
              />
              <span className="text-[var(--text-muted)]">拆</span>
              <input
                type="number"
                step="0.001"
                min="0.001"
                value={newToShares}
                onChange={(e) => setNewToShares(e.target.value)}
                placeholder="4"
                className="input-dark w-16 text-center"
                disabled={isAdding}
              />
            </div>
          </div>
          <div className="flex items-end">
            <button
              type="button"
              onClick={handleAdd}
              disabled={isAdding || !newSymbol.trim() || !newSplitDate || !newToShares}
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

      {/* Split List */}
      {splits.length === 0 ? (
        <p className="text-sm text-[var(--text-muted)] text-center py-4">
          尚未新增股票分割
        </p>
      ) : (
        <div className="divide-y divide-[var(--border-color)]">
          {splits.map((s) => (
            <div key={s.id} className="py-3">
              {editingId === s.id ? (
                // Edit mode
                <div className="grid grid-cols-1 sm:grid-cols-5 gap-3 items-center">
                  <div className="flex items-center gap-2">
                    <span className="font-medium text-[var(--text-primary)]">{s.symbol}</span>
                    <span className="text-xs px-2 py-0.5 rounded bg-[var(--bg-tertiary)] text-[var(--text-muted)]">
                      {MARKET_LABELS[s.market]}
                    </span>
                  </div>
                  <div>
                    <input
                      type="date"
                      value={editSplitDate}
                      onChange={(e) => setEditSplitDate(e.target.value)}
                      className="input-dark w-full text-sm"
                      disabled={isUpdating}
                    />
                  </div>
                  <div>
                    <div className="flex items-center gap-1">
                      <input
                        type="number"
                        step="1"
                        min="1"
                        value={editFromShares}
                        onChange={(e) => setEditFromShares(e.target.value)}
                        className="input-dark w-12 text-center text-sm"
                        disabled={isUpdating}
                      />
                      <span className="text-[var(--text-muted)]">拆</span>
                      <input
                        type="number"
                        step="0.001"
                        min="0.001"
                        value={editToShares}
                        onChange={(e) => setEditToShares(e.target.value)}
                        className="input-dark w-16 text-center text-sm"
                        disabled={isUpdating}
                      />
                    </div>
                  </div>
                  <div className="flex items-center gap-2 sm:col-span-2 justify-end">
                    <button
                      type="button"
                      onClick={() => handleUpdate(s.id)}
                      disabled={isUpdating}
                      className="p-2 text-[var(--color-success)] hover:bg-[var(--color-success)]/10 rounded transition-colors"
                      title="儲存"
                    >
                      {isUpdating ? (
                        <Loader2 className="w-4 h-4 animate-spin" />
                      ) : (
                        <Check className="w-4 h-4" />
                      )}
                    </button>
                    <button
                      type="button"
                      onClick={cancelEdit}
                      disabled={isUpdating}
                      className="p-2 text-[var(--text-muted)] hover:text-[var(--text-primary)] transition-colors"
                      title="取消"
                    >
                      <X className="w-4 h-4" />
                    </button>
                  </div>
                </div>
              ) : (
                // View mode
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-3 flex-wrap">
                    <span className="font-medium text-[var(--text-primary)]">{s.symbol}</span>
                    <span className="text-xs px-2 py-0.5 rounded bg-[var(--bg-tertiary)] text-[var(--text-muted)]">
                      {MARKET_LABELS[s.market]}
                    </span>
                    <span className="text-sm text-[var(--text-secondary)]">
                      {formatDate(s.splitDate)}
                    </span>
                    <span className="text-sm font-mono text-[var(--accent-peach)]">
                      1拆{s.splitRatio}
                    </span>
                  </div>
                  <div className="flex items-center gap-1">
                    <button
                      type="button"
                      onClick={() => startEdit(s)}
                      className="p-2 text-[var(--text-muted)] hover:text-[var(--accent-peach)] transition-colors"
                      title="編輯"
                    >
                      <Edit2 className="w-4 h-4" />
                    </button>
                    <button
                      type="button"
                      onClick={() => handleDelete(s.id)}
                      className="p-2 text-[var(--text-muted)] hover:text-[var(--color-danger)] transition-colors"
                      title="刪除"
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </div>
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export default StockSplitSettings;
