import { useEffect, useState, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { ArrowLeft, Pencil, Trash2, Download, RefreshCw, Loader2, Info } from 'lucide-react';
import { currencyLedgerApi, currencyTransactionApi, stockPriceApi } from '../services/api';
import { exportCurrencyTransactionsToCsv } from '../services/csvExport';
import { CurrencyTransactionForm } from '../components/currency/CurrencyTransactionForm';
import { CurrencyImportButton } from '../components/import';
import type { CurrencyLedgerSummary, CurrencyTransaction, CreateCurrencyTransactionRequest } from '../types';
import { CurrencyTransactionType } from '../types';

// Cache key for exchange rate
const getRateCacheKey = (from: string, to: string) => `rate_cache_${from}_${to}`;

interface CachedRate {
  rate: number;
  cachedAt: string;
}

// Load cached rate from localStorage (no time limit - always show cached, then refresh)
const loadCachedRate = (from: string, to: string): number | null => {
  try {
    const cached = localStorage.getItem(getRateCacheKey(from, to));
    if (cached) {
      const data: CachedRate = JSON.parse(cached);
      return data.rate;
    }
  } catch {
    // Ignore cache errors
  }
  return null;
};

const transactionTypeLabels: Record<number, string> = {
  [CurrencyTransactionType.ExchangeBuy]: '換匯買入',
  [CurrencyTransactionType.ExchangeSell]: '換匯賣出',
  [CurrencyTransactionType.Interest]: '利息收入',
  [CurrencyTransactionType.Spend]: '消費支出',
  [CurrencyTransactionType.InitialBalance]: '轉入餘額',
  [CurrencyTransactionType.OtherIncome]: '其他收入',
  [CurrencyTransactionType.OtherExpense]: '其他支出',
};

const transactionTypeBadgeClass: Record<number, string> = {
  [CurrencyTransactionType.ExchangeBuy]: 'badge-success',
  [CurrencyTransactionType.ExchangeSell]: 'badge-danger',
  [CurrencyTransactionType.Interest]: 'badge-butter',
  [CurrencyTransactionType.Spend]: 'badge-peach',
  [CurrencyTransactionType.InitialBalance]: 'badge-cream',
  [CurrencyTransactionType.OtherIncome]: 'badge-blush',
  [CurrencyTransactionType.OtherExpense]: 'badge-warning',
};

// Calculate balance change for a transaction
function getBalanceChange(tx: CurrencyTransaction): number {
  switch (tx.transactionType) {
    case CurrencyTransactionType.ExchangeBuy:
    case CurrencyTransactionType.InitialBalance:
    case CurrencyTransactionType.Interest:
    case CurrencyTransactionType.OtherIncome:
      return tx.foreignAmount;
    case CurrencyTransactionType.ExchangeSell:
    case CurrencyTransactionType.Spend:
    case CurrencyTransactionType.OtherExpense:
      return -tx.foreignAmount;
    default:
      return 0;
  }
}

// Calculate running balances for transactions (sorted by date)
function calculateRunningBalances(transactions: CurrencyTransaction[]): Map<string, number> {
  const balanceMap = new Map<string, number>();
  let runningBalance = 0;

  for (const tx of transactions) {
    runningBalance += getBalanceChange(tx);
    balanceMap.set(tx.id, runningBalance);
  }

  return balanceMap;
}

export default function CurrencyDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [ledger, setLedger] = useState<CurrencyLedgerSummary | null>(null);
  const [transactions, setTransactions] = useState<CurrencyTransaction[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showAddForm, setShowAddForm] = useState(false);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [isDeleting, setIsDeleting] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const [editingTransaction, setEditingTransaction] = useState<CurrencyTransaction | null>(null);
  const [isEditingName, setIsEditingName] = useState(false);
  const [editName, setEditName] = useState('');
  const [lastSelectedIndex, setLastSelectedIndex] = useState<number | null>(null);
  const [currentRate, setCurrentRate] = useState<number | null>(null);
  const [isFetchingRate, setIsFetchingRate] = useState(false);
  const hasFetchedRate = useRef(false);

  const loadData = async () => {
    if (!id) return;
    try {
      setLoading(true);
      const [ledgerData, txData] = await Promise.all([
        currencyLedgerApi.getById(id),
        currencyTransactionApi.getByLedger(id),
      ]);
      setLedger(ledgerData);
      setTransactions(txData);
      setSelectedIds(new Set());

      // Load cached rate immediately for initial display
      const cachedRate = loadCachedRate(ledgerData.ledger.currencyCode, ledgerData.ledger.homeCurrency);
      if (cachedRate !== null) {
        setCurrentRate(cachedRate);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load data');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadData();
  }, [id]);

  const handleFetchRate = async () => {
    if (!ledger) return;
    setIsFetchingRate(true);
    try {
      const rateResponse = await stockPriceApi.getExchangeRate(
        ledger.ledger.currencyCode,
        ledger.ledger.homeCurrency
      );
      if (rateResponse?.rate) {
        setCurrentRate(rateResponse.rate);
        // Save to cache
        try {
          localStorage.setItem(
            getRateCacheKey(ledger.ledger.currencyCode, ledger.ledger.homeCurrency),
            JSON.stringify({ rate: rateResponse.rate, cachedAt: new Date().toISOString() })
          );
        } catch {
          // Ignore cache errors
        }
      }
    } catch {
      // Silently fail
    } finally {
      setIsFetchingRate(false);
    }
  };

  // Auto-fetch rate when ledger loads (always fetch fresh, use cache only for initial display)
  useEffect(() => {
    if (ledger && !hasFetchedRate.current) {
      hasFetchedRate.current = true;
      handleFetchRate();
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ledger]);

  const handleStartEditName = () => {
    if (ledger) {
      setEditName(ledger.ledger.name);
      setIsEditingName(true);
    }
  };

  const handleSaveName = async () => {
    if (!ledger || !editName.trim()) return;
    try {
      await currencyLedgerApi.update(ledger.ledger.id, { name: editName.trim() });
      setIsEditingName(false);
      await loadData();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update name');
    }
  };

  const handleAddTransaction = async (data: CreateCurrencyTransactionRequest) => {
    await currencyTransactionApi.create(data);
    setShowAddForm(false);
    await loadData();
  };

  const handleSelectAll = () => {
    if (selectedIds.size === transactions.length) {
      setSelectedIds(new Set());
    } else {
      setSelectedIds(new Set(transactions.map(tx => tx.id)));
    }
  };

  const handleSelectOne = (txId: string, index: number, event?: React.MouseEvent) => {
    const newSelected = new Set(selectedIds);

    if (event?.shiftKey && lastSelectedIndex !== null) {
      // Shift+click: select range
      const start = Math.min(lastSelectedIndex, index);
      const end = Math.max(lastSelectedIndex, index);
      for (let i = start; i <= end; i++) {
        newSelected.add(transactions[i].id);
      }
    } else if (event?.ctrlKey || event?.metaKey) {
      // Ctrl/Cmd+click: toggle single item
      if (newSelected.has(txId)) {
        newSelected.delete(txId);
      } else {
        newSelected.add(txId);
      }
    } else {
      // Regular click: toggle single item
      if (newSelected.has(txId)) {
        newSelected.delete(txId);
      } else {
        newSelected.add(txId);
      }
    }

    setSelectedIds(newSelected);
    setLastSelectedIndex(index);
  };

  const handleBatchDelete = async () => {
    setIsDeleting(true);
    try {
      for (const txId of selectedIds) {
        await currencyTransactionApi.delete(txId);
      }
      setShowDeleteConfirm(false);
      await loadData();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete');
    } finally {
      setIsDeleting(false);
    }
  };

  const handleDeleteSingle = async (txId: string) => {
    if (!confirm('確定要刪除這筆交易嗎？')) return;
    try {
      await currencyTransactionApi.delete(txId);
      await loadData();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete');
    }
  };

  const handleEditTransaction = async (data: CreateCurrencyTransactionRequest) => {
    if (!editingTransaction) return;
    try {
      // Delete old transaction and create new one (since we don't have an update API)
      await currencyTransactionApi.delete(editingTransaction.id);
      await currencyTransactionApi.create(data);
      setEditingTransaction(null);
      await loadData();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update');
    }
  };

  const handleExportTransactions = () => {
    if (!ledger || transactions.length === 0) return;
    exportCurrencyTransactionsToCsv(
      transactions,
      ledger.ledger.currencyCode,
      ledger.ledger.homeCurrency
    );
  };

  const formatNumber = (value: number | null | undefined, decimals = 2) => {
    if (value == null) return '-';
    return value.toLocaleString('zh-TW', {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    });
  };

  // Format TWD as integer
  const formatTWD = (value: number | null | undefined) => {
    if (value == null) return '-';
    return Math.round(value).toLocaleString('zh-TW');
  };

  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleDateString('zh-TW');
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-[var(--text-muted)] text-lg">載入中...</div>
      </div>
    );
  }

  if (!ledger) {
    return (
      <div className="max-w-6xl mx-auto px-4 py-8">
        <p className="text-[var(--color-danger)] text-lg">找不到帳本</p>
        <button
          onClick={() => navigate('/currency')}
          className="text-[var(--accent-peach)] hover:underline mt-2 text-base"
        >
          返回列表
        </button>
      </div>
    );
  }

  const isAllSelected = transactions.length > 0 && selectedIds.size === transactions.length;

  return (
    <div className="min-h-screen py-8">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        {/* Back Button */}
        <button
          onClick={() => navigate('/currency')}
          className="flex items-center gap-2 text-[var(--text-secondary)] hover:text-[var(--text-primary)] mb-6 text-base transition-colors"
        >
          <ArrowLeft className="w-5 h-5" />
          返回外幣帳本
        </button>

        {/* Error Alert */}
        {error && (
          <div className="bg-[var(--color-danger-soft)] border border-[var(--color-danger)] text-[var(--color-danger)] p-4 rounded-lg mb-6 flex justify-between items-center">
            <span className="text-base">{error}</span>
            <button onClick={() => setError(null)} className="hover:underline text-base">關閉</button>
          </div>
        )}

        {/* Summary Card */}
        <div className="card-dark p-6 mb-6">
          <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 mb-6">
            <div>
              <h1 className="text-2xl font-bold text-[var(--text-primary)]">
                {ledger.ledger.currencyCode}
              </h1>
              {isEditingName ? (
                <div className="flex items-center gap-2 mt-1">
                  <input
                    type="text"
                    value={editName}
                    onChange={(e) => setEditName(e.target.value)}
                    className="input-dark py-1 px-2 text-base"
                    autoFocus
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') handleSaveName();
                      if (e.key === 'Escape') setIsEditingName(false);
                    }}
                  />
                  <button
                    onClick={handleSaveName}
                    className="btn-accent py-1 px-3 text-sm"
                  >
                    儲存
                  </button>
                  <button
                    onClick={() => setIsEditingName(false)}
                    className="btn-dark py-1 px-3 text-sm"
                  >
                    取消
                  </button>
                </div>
              ) : (
                <div className="flex items-center gap-2 mt-1">
                  <p className="text-[var(--text-muted)] text-base">{ledger.ledger.name}</p>
                  <button
                    onClick={handleStartEditName}
                    className="p-1 text-[var(--text-muted)] hover:text-[var(--accent-butter)] hover:bg-[var(--bg-hover)] rounded transition-colors"
                    title="編輯名稱"
                  >
                    <Pencil className="w-4 h-4" />
                  </button>
                </div>
              )}
            </div>
          </div>

          <div className="grid grid-cols-2 lg:grid-cols-5 gap-4">
            <div className="metric-card">
              <p className="text-[var(--text-muted)] text-sm mb-1">餘額</p>
              <p className="text-2xl font-bold text-[var(--text-primary)] number-display">
                {formatNumber(ledger.balance, 4)}
              </p>
              <p className="text-[var(--text-muted)] text-sm">{ledger.ledger.currencyCode}</p>
              {currentRate && ledger.balance > 0 && (
                <p className="text-[var(--accent-peach)] text-sm mt-1">
                  ≈ {formatTWD(ledger.balance * currentRate)} TWD
                </p>
              )}
            </div>
            <div className="metric-card">
              <div className="flex items-center gap-1 mb-1">
                <p className="text-[var(--text-muted)] text-sm">即時匯率</p>
                <button
                  onClick={handleFetchRate}
                  disabled={isFetchingRate}
                  className="p-0.5 text-[var(--text-muted)] hover:text-[var(--accent-butter)] transition-colors disabled:opacity-50"
                  title="更新匯率"
                >
                  {isFetchingRate ? (
                    <Loader2 className="w-3 h-3 animate-spin" />
                  ) : (
                    <RefreshCw className="w-3 h-3" />
                  )}
                </button>
              </div>
              <p className="text-2xl font-bold text-[var(--text-primary)] number-display">
                {currentRate ? formatNumber(currentRate, 4) : '-'}
              </p>
              {currentRate && ledger.averageExchangeRate && (
                <p className={`text-sm ${currentRate > ledger.averageExchangeRate ? 'text-red-400' : 'text-green-400'}`}>
                  {currentRate > ledger.averageExchangeRate ? '↑' : '↓'} vs 均價
                </p>
              )}
            </div>
            <div className="metric-card">
              <p className="text-[var(--text-muted)] text-sm mb-1">換匯均價</p>
              <p className="text-2xl font-bold text-[var(--text-primary)] number-display">
                {formatNumber(ledger.averageExchangeRate, 4)}
              </p>
            </div>
            <div className="metric-card">
              <p className="text-[var(--text-muted)] text-sm mb-1">淨投入</p>
              <p className="text-2xl font-bold text-[var(--text-primary)] number-display">
                {formatTWD(ledger.totalExchanged)}
              </p>
              <p className="text-[var(--text-muted)] text-sm">{ledger.ledger.homeCurrency}</p>
            </div>
            <div className="metric-card">
              <div className="flex items-center gap-1 mb-1">
                <p className="text-[var(--text-muted)] text-sm">股票投入</p>
                <div className="relative group">
                  <Info className="w-3 h-3 text-[var(--text-muted)] cursor-help" />
                  <div className="absolute left-0 bottom-full mb-2 hidden group-hover:block z-10">
                    <div className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg p-2 shadow-lg text-xs text-[var(--text-secondary)] whitespace-nowrap">
                      從此帳本用於購買股票的外幣金額
                    </div>
                  </div>
                </div>
              </div>
              <p className="text-2xl font-bold text-[var(--text-primary)] number-display">
                {formatNumber(ledger.totalSpentOnStocks, 4)}
              </p>
              <p className="text-[var(--text-muted)] text-sm">{ledger.ledger.currencyCode}</p>
            </div>
          </div>
        </div>

        {/* Add Transaction Modal */}
        {showAddForm && (
          <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50">
            <div className="card-dark p-6 w-full max-w-md max-h-[90vh] overflow-y-auto m-4">
              <h2 className="text-xl font-bold text-[var(--text-primary)] mb-4">新增交易</h2>
              <CurrencyTransactionForm
                ledgerId={ledger.ledger.id}
                onSubmit={handleAddTransaction}
                onCancel={() => setShowAddForm(false)}
              />
            </div>
          </div>
        )}

        {/* Delete Confirmation Modal */}
        {showDeleteConfirm && (
          <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50">
            <div className="card-dark p-6 w-full max-w-md m-4">
              <h2 className="text-xl font-bold text-[var(--color-danger)] mb-4">確認刪除</h2>
              <p className="text-[var(--text-primary)] text-base mb-2">
                您確定要刪除選取的 <strong>{selectedIds.size}</strong> 筆交易嗎？
              </p>
              <p className="text-[var(--color-danger)] text-sm mb-6">此操作無法復原！</p>
              <div className="flex gap-3">
                <button
                  onClick={() => setShowDeleteConfirm(false)}
                  disabled={isDeleting}
                  className="btn-dark flex-1 disabled:opacity-50"
                >
                  取消
                </button>
                <button
                  onClick={handleBatchDelete}
                  disabled={isDeleting}
                  className="btn-danger flex-1 disabled:opacity-50"
                >
                  {isDeleting ? '刪除中...' : '確認刪除'}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Edit Transaction Modal */}
        {editingTransaction && (
          <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50">
            <div className="card-dark p-6 w-full max-w-md max-h-[90vh] overflow-y-auto m-4">
              <h2 className="text-xl font-bold text-[var(--text-primary)] mb-4">編輯交易</h2>
              <CurrencyTransactionForm
                ledgerId={ledger.ledger.id}
                initialData={editingTransaction}
                onSubmit={handleEditTransaction}
                onCancel={() => setEditingTransaction(null)}
              />
            </div>
          </div>
        )}

        {/* Transaction List */}
        <div className="card-dark overflow-hidden">
          <div className="flex justify-between items-center p-5 border-b border-[var(--border-color)]">
            <h2 className="text-lg font-bold text-[var(--text-primary)]">交易紀錄</h2>
            <div className="flex items-center gap-2">
              {selectedIds.size > 0 && (
                <button
                  onClick={() => setShowDeleteConfirm(true)}
                  className="btn-danger text-sm"
                >
                  刪除選取 ({selectedIds.size})
                </button>
              )}
              <button
                onClick={handleExportTransactions}
                disabled={transactions.length === 0}
                className="btn-dark flex items-center gap-2 px-3 py-1.5 text-sm disabled:opacity-50"
                title="匯出交易"
              >
                <Download className="w-4 h-4" />
                匯出
              </button>
              <button
                onClick={() => setShowAddForm(true)}
                className="btn-accent px-3 py-1.5 text-sm"
              >
                + 新增交易
              </button>
              <CurrencyImportButton
                ledgerId={ledger.ledger.id}
                onImportComplete={loadData}
              />
            </div>
          </div>

          {transactions.length === 0 ? (
            <p className="text-[var(--text-muted)] text-center py-12 text-base">尚無交易紀錄</p>
          ) : (
            <div className="overflow-x-auto max-h-[60vh] overflow-y-auto">
              <table className="table-dark">
                <thead className="sticky top-0 z-10">
                  <tr>
                    <th className="w-12 text-center">
                      <input
                        type="checkbox"
                        checked={isAllSelected}
                        onChange={handleSelectAll}
                        className="checkbox-dark"
                      />
                    </th>
                    <th>日期</th>
                    <th>類型</th>
                    <th className="text-right">外幣金額</th>
                    <th className="text-right">台幣金額</th>
                    <th className="text-right">匯率</th>
                    <th className="text-right">餘額</th>
                    <th>備註</th>
                    <th className="w-24 text-center">操作</th>
                  </tr>
                </thead>
                <tbody>
                  {(() => {
                    const runningBalances = calculateRunningBalances(transactions);
                    return transactions.map((tx, index) => (
                      <tr
                        key={tx.id}
                        className={selectedIds.has(tx.id) ? 'bg-[var(--accent-peach-soft)]' : ''}
                      >
                        <td className="text-center">
                          <input
                            type="checkbox"
                            checked={selectedIds.has(tx.id)}
                            onClick={(e) => handleSelectOne(tx.id, index, e)}
                            onChange={() => {}}
                            className="checkbox-dark cursor-pointer"
                          />
                        </td>
                        <td className="whitespace-nowrap">{formatDate(tx.transactionDate)}</td>
                        <td>
                          <span className={`badge ${transactionTypeBadgeClass[tx.transactionType]}`}>
                            {transactionTypeLabels[tx.transactionType]}
                          </span>
                        </td>
                        <td className="text-right number-display whitespace-nowrap">
                          {formatNumber(tx.foreignAmount, 4)}
                        </td>
                        <td className="text-right number-display whitespace-nowrap">
                          {tx.homeAmount ? formatNumber(tx.homeAmount) : '-'}
                        </td>
                        <td className="text-right number-display whitespace-nowrap">
                          {tx.exchangeRate ? formatNumber(tx.exchangeRate, 4) : '-'}
                        </td>
                        <td className="text-right number-display whitespace-nowrap">
                          {formatNumber(runningBalances.get(tx.id) ?? 0, 4)}
                        </td>
                        <td className="text-[var(--text-muted)]">
                          {tx.notes || '-'}
                        </td>
                        <td className="text-center">
                          <div className="flex justify-center gap-2">
                            <button
                              onClick={() => setEditingTransaction(tx)}
                              className="p-1.5 text-[var(--text-muted)] hover:text-[var(--accent-butter)] hover:bg-[var(--bg-hover)] rounded transition-colors"
                              title="編輯"
                            >
                              <Pencil className="w-4 h-4" />
                            </button>
                            <button
                              onClick={() => handleDeleteSingle(tx.id)}
                              className="p-1.5 text-[var(--text-muted)] hover:text-[var(--color-danger)] hover:bg-[var(--bg-hover)] rounded transition-colors"
                              title="刪除"
                            >
                              <Trash2 className="w-4 h-4" />
                            </button>
                          </div>
                        </td>
                      </tr>
                    ));
                  })()}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
