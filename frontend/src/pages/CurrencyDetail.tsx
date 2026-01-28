/**
 * Currency Detail Page
 *
 * 外幣帳本詳情頁：顯示單一外幣帳本的交易明細、批次選取/刪除、匯入/匯出，以及即時匯率顯示。
 *
 * 特色：
 * - 先用 localStorage 匯率快取做初始顯示，再自動抓取最新匯率。
 * - 交易列表支援 Shift 範圍選取與 Ctrl/Cmd 單筆切換。
 */
import { useEffect, useState, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { ArrowLeft, Pencil, Trash2, RefreshCw, Info } from 'lucide-react';
import { currencyLedgerApi, currencyTransactionApi, stockPriceApi } from '../services/api';
import { exportCurrencyTransactionsToCsv } from '../services/csvExport';
import { CurrencyTransactionForm } from '../components/currency/CurrencyTransactionForm';
import { CurrencyImportButton } from '../components/import';
import { FileDropdown } from '../components/common';
import type { CurrencyLedgerSummary, CurrencyTransaction, CreateCurrencyTransactionRequest } from '../types';
import { CurrencyTransactionType } from '../types';

/**
 * 匯率 localStorage 快取 key。
 * @param from 外幣幣別（例如 USD）
 * @param to 本位幣幣別（例如 TWD）
 */
const getRateCacheKey = (from: string, to: string) => `rate_cache_${from}_${to}`;

interface CachedRate {
  rate: number;
  cachedAt: string;
}

/**
 * 從 localStorage 載入匯率快取。
 *
 * 設計：不限制快取時效，先顯示快取，再於 ledger 載入後自動抓最新匯率。
 */
const loadCachedRate = (from: string, to: string): CachedRate | null => {
  try {
    const cached = localStorage.getItem(getRateCacheKey(from, to));
    if (cached) {
      return JSON.parse(cached);
    }
  } catch {
    // Ignore cache errors
  }
  return null;
};

const transactionTypeLabels: Record<number, string> = {
  [CurrencyTransactionType.ExchangeBuy]: '換匯買入',
  [CurrencyTransactionType.ExchangeSell]: '換匯賣出',
  [CurrencyTransactionType.Deposit]: '存入（入金）',
  [CurrencyTransactionType.Withdraw]: '提領（出金）',
  [CurrencyTransactionType.Interest]: '利息收入',
  [CurrencyTransactionType.Spend]: '消費支出',
  [CurrencyTransactionType.InitialBalance]: '轉入餘額',
  [CurrencyTransactionType.OtherIncome]: '其他收入',
  [CurrencyTransactionType.OtherExpense]: '其他支出',
};

const transactionTypeBadgeClass: Record<number, string> = {
  [CurrencyTransactionType.ExchangeBuy]: 'badge-success',
  [CurrencyTransactionType.ExchangeSell]: 'badge-danger',
  [CurrencyTransactionType.Deposit]: 'badge-success',
  [CurrencyTransactionType.Withdraw]: 'badge-danger',
  [CurrencyTransactionType.Interest]: 'badge-butter',
  [CurrencyTransactionType.Spend]: 'badge-peach',
  [CurrencyTransactionType.InitialBalance]: 'badge-cream',
  [CurrencyTransactionType.OtherIncome]: 'badge-blush',
  [CurrencyTransactionType.OtherExpense]: 'badge-warning',
};

/**
 * 計算單筆交易對外幣餘額的影響（+ 增加 / - 減少）。
 * @param tx 外幣交易
 */
function getBalanceChange(tx: CurrencyTransaction): number {
  switch (tx.transactionType) {
    case CurrencyTransactionType.ExchangeBuy:
    case CurrencyTransactionType.InitialBalance:
    case CurrencyTransactionType.Deposit:
    case CurrencyTransactionType.Interest:
    case CurrencyTransactionType.OtherIncome:
      return tx.foreignAmount;
    case CurrencyTransactionType.ExchangeSell:
    case CurrencyTransactionType.Spend:
    case CurrencyTransactionType.OtherExpense:
    case CurrencyTransactionType.Withdraw:
      return -tx.foreignAmount;
    default:
      return 0;
  }
}

/**
 * 計算每筆交易後的累計餘額（running balance）。
 *
 * 前提：transactions 應已依日期排序（由舊到新），否則累計結果會不符合使用者直覺。
 * @param transactions 外幣交易清單
 */
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
  const [lastSelectedIndex, setLastSelectedIndex] = useState<number | null>(null);
  const [currentRate, setCurrentRate] = useState<number | null>(null);
  const [rateUpdatedAt, setRateUpdatedAt] = useState<Date | null>(null);
  const [isFetchingRate, setIsFetchingRate] = useState(false);
  const hasFetchedRate = useRef(false);
  const importTriggerRef = useRef<(() => void) | null>(null);
  const scrollYRef = useRef<number>(0);

  // Track if we have data loaded (to detect refresh vs initial)
  const isDataLoadedRef = useRef(false);
  if (ledger) isDataLoadedRef.current = true;

  /**
   * 載入帳本摘要與交易清單，並在初次載入時優先套用匯率快取做顯示。
   */
  const loadData = async () => {
    if (!id) return;

    try {
      // 只有在已經有資料的情況下（例如刪除交易後重整），才需要記住 scroll 位置
      if (isDataLoadedRef.current) {
        scrollYRef.current = window.scrollY;
      } else {
        scrollYRef.current = 0;
        setLoading(true);
      }

      const [ledgerData, txData] = await Promise.all([
        currencyLedgerApi.getById(id),
        currencyTransactionApi.getByLedger(id),
      ]);
      setLedger(ledgerData);
      setTransactions(txData);
      setSelectedIds(new Set());

      // Load cached rate immediately for initial display
      const cachedData = loadCachedRate(ledgerData.ledger.currencyCode, ledgerData.ledger.homeCurrency);
      if (cachedData !== null) {
        setCurrentRate(cachedData.rate);
        setRateUpdatedAt(new Date(cachedData.cachedAt));
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load data');
    } finally {
      setLoading(false);
      // Restore scroll position only if we have a stored position > 0 (refresh case)
      if (scrollYRef.current > 0) {
        requestAnimationFrame(() => {
          window.scrollTo({ top: scrollYRef.current });
        });
      }
    }
  };

  useEffect(() => {
    loadData();
  }, [id]);

  /**
   * 取得最新匯率並寫入 state + localStorage 快取。
   */
  const handleFetchRate = async () => {
    if (!ledger) return;
    setIsFetchingRate(true);
    try {
      const rateResponse = await stockPriceApi.getExchangeRate(
        ledger.ledger.currencyCode,
        ledger.ledger.homeCurrency
      );
      if (rateResponse?.rate) {
        const now = new Date();
        setCurrentRate(rateResponse.rate);
        setRateUpdatedAt(now);
        // Save to cache
        try {
          localStorage.setItem(
            getRateCacheKey(ledger.ledger.currencyCode, ledger.ledger.homeCurrency),
            JSON.stringify({ rate: rateResponse.rate, cachedAt: now.toISOString() })
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
      // Skip fetch for home currency
      if (ledger.ledger.currencyCode === ledger.ledger.homeCurrency) return;

      hasFetchedRate.current = true;
      handleFetchRate();
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ledger]);

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

  /**
   * 交易多選互動：
   * - Shift+click：連續範圍選取
   * - Ctrl/Cmd+click：切換單筆選取
   * - 一般 click：切換單筆選取
   */
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

  /**
   * 批次刪除目前選取的交易。
   *
   * 注意：此流程為逐筆 delete（目前無批次 API），刪除後會重新載入資料。
   */
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

  /**
   * 編輯交易：因目前沒有 update API，採「刪除舊交易 + 建立新交易」的方式達成。
   */
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

  /**
   * 匯出外幣交易明細為 CSV。
   */
  const handleExportTransactions = () => {
    if (!ledger || transactions.length === 0) return;
    exportCurrencyTransactionsToCsv(
      transactions,
      ledger.ledger.currencyCode,
      ledger.ledger.homeCurrency
    );
  };

  const formatNumber = (value: number | null | undefined, decimals = 2) => {
    if (value == null || isNaN(value)) return '-';
    return value.toLocaleString('zh-TW', {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    });
  };

  // Format TWD as integer
  const formatTWD = (value: number | null | undefined) => {
    if (value == null || isNaN(value)) return '-';
    return Math.round(value).toLocaleString('zh-TW');
  };

  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleDateString('zh-TW');
  };

  const formatTime = (date: Date) => {
    return date.toLocaleTimeString('zh-TW', { hour: '2-digit', minute: '2-digit', hour12: false });
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

  const isHomeCurrencyLedger = ledger.ledger.currencyCode === ledger.ledger.homeCurrency;
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
          {/* Header with title and update time */}
          <div className="flex justify-between items-center mb-4">
            <div className="flex items-center gap-3">
              <h1 className="text-2xl font-bold text-[var(--accent-cream)]">
                {ledger.ledger.currencyCode}
              </h1>
              {!isHomeCurrencyLedger && (
                <div className="flex items-center gap-1">
                  <span className="text-lg text-[var(--text-muted)]">@</span>
                  <span className="text-lg font-medium text-[var(--accent-peach)]">
                    {currentRate ? formatNumber(currentRate, 2) : (
                      <span className="inline-block w-16">&nbsp;</span>
                    )}
                  </span>
                  <button
                    onClick={handleFetchRate}
                    disabled={isFetchingRate}
                    className="p-1 text-[var(--text-muted)] hover:text-[var(--accent-butter)] transition-colors disabled:opacity-50"
                    title="更新匯率"
                  >
                    <RefreshCw className={`w-4 h-4 ${isFetchingRate ? 'animate-spin' : ''}`} />
                  </button>
                </div>
              )}
            </div>
            {!isHomeCurrencyLedger && rateUpdatedAt && (
              <span className="text-sm text-[var(--text-muted)]">
                匯率更新於 {formatTime(rateUpdatedAt)}
              </span>
            )}
          </div>

          {/* Metrics grid */}
          <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-5 gap-4">
            <div className="metric-card">
              <p className="text-sm text-[var(--text-muted)] mb-1">餘額</p>
              <p className="text-lg font-bold text-[var(--accent-peach)] number-display">
                {formatNumber(ledger.balance, 2)} {ledger.ledger.currencyCode}
              </p>
              {!isHomeCurrencyLedger && currentRate && ledger.balance > 0 && (
                <p className="text-xs text-[var(--text-muted)] mt-1">
                  ≈ {formatTWD(ledger.balance * currentRate)} TWD
                </p>
              )}
            </div>
            {!isHomeCurrencyLedger && (
              <div className="metric-card">
                <p className="text-sm text-[var(--text-muted)] mb-1">換匯均價</p>
                <p className="text-lg font-bold text-[var(--text-primary)] number-display">
                  {formatNumber(ledger.averageExchangeRate, 4)}
                </p>
              </div>
            )}
            {!isHomeCurrencyLedger && (
              <div className="metric-card">
                <p className="text-sm text-[var(--text-muted)] mb-1">淨投入</p>
                <p className="text-lg font-bold text-[var(--text-primary)] number-display">
                  {formatTWD(ledger.totalExchanged)} {ledger.ledger.homeCurrency}
                </p>
              </div>
            )}
            {(ledger.totalInterest ?? 0) > 0 && (
              <div className="metric-card">
                <p className="text-sm text-[var(--text-muted)] mb-1">利息收入</p>
                <p className="text-lg font-bold text-[var(--accent-peach)] number-display">
                  {formatNumber(ledger.totalInterest, 2)} {ledger.ledger.currencyCode}
                </p>
              </div>
            )}
            {(ledger.totalSpentOnStocks ?? 0) > 0 && (
              <div className="metric-card">
                <div className="flex items-center gap-1 mb-1">
                  <p className="text-sm text-[var(--text-muted)]">股票投入</p>
                  <div className="relative group">
                    <Info className="w-3 h-3 text-[var(--text-muted)] cursor-help" />
                    <div className="absolute left-0 bottom-full mb-2 hidden group-hover:block z-10">
                      <div className="bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg p-2 shadow-lg text-xs text-[var(--text-secondary)] whitespace-nowrap">
                        從此帳本用於購買股票的外幣金額
                      </div>
                    </div>
                  </div>
                </div>
                <p className="text-lg font-bold text-[var(--text-primary)] number-display">
                  {formatNumber(ledger.totalSpentOnStocks, 2)} {ledger.ledger.currencyCode}
                </p>
              </div>
            )}
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
              <FileDropdown
                onImport={() => importTriggerRef.current?.()}
                onExport={handleExportTransactions}
                exportDisabled={transactions.length === 0}
              />
              <button
                onClick={() => setShowAddForm(true)}
                className="btn-accent px-3 py-1.5 text-sm"
              >
                + 新增
              </button>
              <CurrencyImportButton
                ledgerId={ledger.ledger.id}
                onImportComplete={loadData}
                renderTrigger={(onClick) => {
                  importTriggerRef.current = onClick;
                  return null;
                }}
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
                    // Display in reverse order (newest first), calculate original index for selection
                    return [...transactions].reverse().map((tx, displayIndex) => {
                      const originalIndex = transactions.length - 1 - displayIndex;
                      return (
                      <tr
                        key={tx.id}
                        className={selectedIds.has(tx.id) ? 'bg-[var(--accent-peach-soft)]' : ''}
                      >
                        <td className="text-center">
                          <input
                            type="checkbox"
                            checked={selectedIds.has(tx.id)}
                            onClick={(e) => handleSelectOne(tx.id, originalIndex, e)}
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
                          {tx.relatedStockTransactionId ? (
                            <span className="text-xs text-[var(--text-muted)]" title="此交易由股票買入自動產生，無法直接編輯或刪除">
                              🔒
                            </span>
                          ) : (
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
                          )}
                        </td>
                      </tr>
                    );});
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
