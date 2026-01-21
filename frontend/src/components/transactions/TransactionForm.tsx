/**
 * TransactionForm
 *
 * 股票交易新增/編輯表單：支援買入/賣出/分割/調整，並可選擇資金來源（例如外幣帳本）。
 *
 * 重要行為：
 * - 台股會自動將匯率補為 1（若使用者未填）。
 * - 若資金來源為 CurrencyLedger 且不是台股，則匯率欄位可省略，交由 backend 依帳本推導。
 * - ForeignCurrency portfolio 也不使用匯率欄位。
 */
import { useState, useEffect } from 'react';
import { currencyLedgerApi } from '../../services/api';
import type { CreateStockTransactionRequest, StockTransaction, TransactionType, FundSource, CurrencyLedgerSummary, StockMarket } from '../../types';
import { FundSource as FundSourceEnum, StockMarket as StockMarketEnum } from '../../types';

/**
 * 判斷是否為台股 ticker（純數字或數字+英文字尾）。
 */
const isTaiwanStock = (ticker: string): boolean => {
  return /^\d+[A-Za-z]*$/.test(ticker.trim());
};

/**
 * 根據 ticker 推測市場
 */
const guessMarketFromTicker = (ticker: string): StockMarket => {
  if (!ticker) return StockMarketEnum.US;
  const trimmed = ticker.trim().toUpperCase();
  // 台股：數字開頭
  if (/^\d/.test(trimmed)) return StockMarketEnum.TW;
  // 英股：.L 結尾
  if (trimmed.endsWith('.L')) return StockMarketEnum.UK;
  // 預設美股
  return StockMarketEnum.US;
};

interface TransactionFormProps {
  /** 目標 portfolio ID */
  portfolioId: string;
  /** 編輯模式的初始資料 */
  initialData?: StockTransaction;
  /** 送出表單 callback */
  onSubmit: (data: CreateStockTransactionRequest) => Promise<void>;
  /** 取消 callback */
  onCancel?: () => void;
  /** 當為 true 時，隱藏匯率欄位（ForeignCurrency portfolios 使用 source currency） */
  isForeignCurrencyPortfolio?: boolean;
}

export function TransactionForm({ portfolioId, initialData, onSubmit, onCancel, isForeignCurrencyPortfolio = false }: TransactionFormProps) {
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [currencyLedgers, setCurrencyLedgers] = useState<CurrencyLedgerSummary[]>([]);
  const [selectedLedger, setSelectedLedger] = useState<CurrencyLedgerSummary | null>(null);

  const [formData, setFormData] = useState({
    ticker: initialData?.ticker ?? '',
    transactionType: (initialData?.transactionType ?? 1) as TransactionType,
    transactionDate: initialData?.transactionDate?.split('T')[0] ?? new Date().toISOString().split('T')[0],
    shares: initialData?.shares?.toString() ?? '',
    pricePerShare: initialData?.pricePerShare?.toString() ?? '',
    exchangeRate: initialData?.exchangeRate?.toString() ?? '',
    fees: initialData?.fees?.toString() ?? '0',
    fundSource: (initialData?.fundSource ?? FundSourceEnum.None) as FundSource,
    currencyLedgerId: initialData?.currencyLedgerId ?? '',
    notes: initialData?.notes ?? '',
    market: (initialData?.market ?? guessMarketFromTicker(initialData?.ticker ?? '')) as StockMarket,
  });

  // 載入外幣帳本清單，供資金來源選擇（CurrencyLedger）。
  useEffect(() => {
    const loadLedgers = async () => {
      try {
        const ledgers = await currencyLedgerApi.getAll();
        setCurrencyLedgers(ledgers);
      } catch {
        console.error('Failed to load currency ledgers');
      }
    };
    loadLedgers();
  }, []);

  // Update selected ledger when currencyLedgerId changes
  useEffect(() => {
    if (formData.currencyLedgerId) {
      const ledger = currencyLedgers.find(l => l.ledger.id === formData.currencyLedgerId);
      setSelectedLedger(ledger || null);
    } else {
      setSelectedLedger(null);
    }
  }, [formData.currencyLedgerId, currencyLedgers]);

  // Derived state: is current ticker a Taiwan stock?
  const isTW = isTaiwanStock(formData.ticker);

  // Derived state: is using currency ledger for non-TW stock?
  const useCurrencyLedger = formData.fundSource === FundSourceEnum.CurrencyLedger && !isTW;

  /**
   * 表單欄位更新。
   *
   * 特殊規則：當使用者輸入台股 ticker 且匯率尚未填寫時，會自動補上 `1`。
   */
  const handleChange = (
    e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>
  ) => {
    const { name, value } = e.target;
    setFormData((prev) => {
      const newData = { ...prev, [name]: value };
      // Auto-set exchange rate to 1 for Taiwan stocks
      if (name === 'ticker' && isTaiwanStock(value) && !prev.exchangeRate) {
        newData.exchangeRate = '1';
      }
      // Auto-set market based on ticker
      if (name === 'ticker') {
        newData.market = guessMarketFromTicker(value);
      }
      return newData;
    });
  };

  // Handle ticker blur to auto-set exchange rate for Taiwan stocks
  const handleTickerBlur = () => {
    if (isTaiwanStock(formData.ticker) && !formData.exchangeRate) {
      setFormData((prev) => ({ ...prev, exchangeRate: '1' }));
    }
  };

  /**
   * 資金來源變更：若切換到 CurrencyLedger，預設選第一個 ledger；切換離開則清空 ledgerId。
   */
  const handleFundSourceChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const value = Number(e.target.value) as FundSource;
    setFormData((prev) => ({
      ...prev,
      fundSource: value,
      // When switching to CurrencyLedger, default to first ledger; when switching away, clear selection
      currencyLedgerId: value === FundSourceEnum.CurrencyLedger
        ? (currencyLedgers[0]?.ledger.id ?? '')
        : '',
    }));
  };

  /**
   * 計算顯示用的「預估交易金額」：shares * price + fees。
   */
  const calculateRequiredAmount = () => {
    const shares = parseFloat(formData.shares) || 0;
    const price = parseFloat(formData.pricePerShare) || 0;
    const fees = parseFloat(formData.fees) || 0;
    return (shares * price) + fees;
  };

  /**
   * 表單送出：整理欄位、決定是否省略 exchangeRate，然後呼叫外部 `onSubmit`。
   * @param e React 表單事件
   */
  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setIsSubmitting(true);

    try {
      // When using currency ledger (and not TW stock), don't send exchange rate - backend will calculate
      // ForeignCurrency portfolios also don't use exchange rate
      const shouldOmitExchangeRate = isForeignCurrencyPortfolio || (formData.fundSource === FundSourceEnum.CurrencyLedger && !isTW);

      // Taiwan stocks always use exchange rate 1
      // For other stocks, parse the value or use undefined if empty/invalid
      let exchangeRateValue: number | undefined;
      if (isTW) {
        exchangeRateValue = 1;
      } else if (shouldOmitExchangeRate) {
        exchangeRateValue = undefined;
      } else {
        const parsed = parseFloat(formData.exchangeRate);
        exchangeRateValue = !formData.exchangeRate || isNaN(parsed) ? undefined : parsed;
      }

      const request: CreateStockTransactionRequest = {
        portfolioId,
        ticker: formData.ticker.toUpperCase(),
        transactionType: Number(formData.transactionType) as TransactionType,
        transactionDate: formData.transactionDate,
        shares: parseFloat(formData.shares),
        pricePerShare: parseFloat(formData.pricePerShare),
        exchangeRate: exchangeRateValue,
        fees: parseFloat(formData.fees) || 0,
        fundSource: formData.fundSource,
        currencyLedgerId: formData.currencyLedgerId || undefined,
        notes: formData.notes || undefined,
        market: formData.market,
      };

      await onSubmit(request);

      // Reset form
      setFormData({
        ticker: '',
        transactionType: 1 as TransactionType,
        transactionDate: new Date().toISOString().split('T')[0],
        shares: '',
        pricePerShare: '',
        exchangeRate: '',
        fees: '0',
        fundSource: FundSourceEnum.None as FundSource,
        currencyLedgerId: '',
        notes: '',
        market: StockMarketEnum.US as StockMarket,
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create transaction');
    } finally {
      setIsSubmitting(false);
    }
  };

  const requiredAmount = calculateRequiredAmount();

  // When editing, add back the original transaction amount to the effective balance
  const originalAmount = initialData
    ? (initialData.shares * initialData.pricePerShare + initialData.fees)
    : 0;
  const effectiveBalance = selectedLedger
    ? selectedLedger.balance + originalAmount
    : 0;
  const hasInsufficientBalance = selectedLedger && requiredAmount > effectiveBalance;

  return (
    <form onSubmit={handleSubmit} className="card-dark space-y-5 p-6">
      <h3 className="text-lg font-bold text-[var(--text-primary)]">{initialData ? '編輯交易' : '新增交易'}</h3>

      {error && (
        <div className="p-3 bg-[var(--color-danger-soft)] border border-[var(--color-danger)] text-[var(--color-danger)] rounded-lg text-base">{error}</div>
      )}

      <div className="grid grid-cols-2 gap-4">
        <div>
          <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
            股票代號
            {isTW && <span className="ml-2 text-xs text-[var(--accent-peach)]">台股</span>}
          </label>
          <input
            type="text"
            name="ticker"
            value={formData.ticker}
            onChange={handleChange}
            onBlur={handleTickerBlur}
            required
            maxLength={20}
            className="input-dark w-full"
            placeholder="例如：VWRA, 2330"
          />
        </div>

        <div>
          <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
            交易類型
          </label>
          <select
            name="transactionType"
            value={formData.transactionType}
            onChange={handleChange}
            className="input-dark w-full"
          >
            <option value={1}>買入</option>
            <option value={2}>賣出</option>
          </select>
        </div>

        <div>
          <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
            市場
          </label>
          <select
            name="market"
            value={formData.market}
            onChange={(e) => setFormData(prev => ({ ...prev, market: Number(e.target.value) as StockMarket }))}
            className="input-dark w-full"
          >
            <option value={StockMarketEnum.TW}>台股</option>
            <option value={StockMarketEnum.US}>美股</option>
            <option value={StockMarketEnum.UK}>英股</option>
            <option value={StockMarketEnum.EU}>歐股</option>
          </select>
        </div>

        <div>
          <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
            日期
          </label>
          <input
            type="date"
            name="transactionDate"
            value={formData.transactionDate}
            onChange={handleChange}
            required
            className="input-dark w-full"
          />
        </div>

        <div>
          <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
            股數
          </label>
          <input
            type="number"
            name="shares"
            value={formData.shares}
            onChange={handleChange}
            required
            min="0.0001"
            step="0.0001"
            className="input-dark w-full"
            placeholder="10.5"
          />
        </div>

        <div>
          <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
            價格
          </label>
          <input
            type="number"
            name="pricePerShare"
            value={formData.pricePerShare}
            onChange={handleChange}
            required
            min="0"
            step="0.00001"
            className="input-dark w-full"
            placeholder="100.00"
          />
        </div>

        {/* Exchange rate - hidden for Taiwan stocks, currency ledger, and ForeignCurrency portfolios */}
        {!useCurrencyLedger && !isTW && !isForeignCurrencyPortfolio && (
          <div>
            <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
              匯率（選填）
              <span className="ml-2 text-xs text-[var(--text-muted)]">不填則自動以交易日匯率計算</span>
            </label>
            <input
              type="number"
              name="exchangeRate"
              value={formData.exchangeRate}
              onChange={handleChange}
              min="0.000001"
              step="0.000001"
              className="input-dark w-full"
              placeholder="留空自動填補"
            />
          </div>
        )}

        <div>
          <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
            手續費
          </label>
          <input
            type="number"
            name="fees"
            value={formData.fees}
            onChange={handleChange}
            min="0"
            step="0.01"
            className="input-dark w-full"
            placeholder="0"
          />
        </div>
      </div>

      {/* Fund Source Section */}
      <div className="border-t border-[var(--border-color)] pt-5 mt-5">
        <h4 className="text-base font-medium text-[var(--text-secondary)] mb-4">資金來源</h4>
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
              來源
            </label>
            <select
              name="fundSource"
              value={formData.fundSource}
              onChange={handleFundSourceChange}
              className="input-dark w-full"
            >
              <option value={FundSourceEnum.None}>外部資金（不追蹤）</option>
              <option value={FundSourceEnum.CurrencyLedger}>外幣帳本</option>
            </select>
          </div>

          {formData.fundSource === FundSourceEnum.CurrencyLedger && (
            <div>
              <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                外幣帳本
              </label>
              <select
                name="currencyLedgerId"
                value={formData.currencyLedgerId}
                onChange={handleChange}
                required
                className="input-dark w-full"
              >
                {currencyLedgers.map((ledger) => (
                  <option key={ledger.ledger.id} value={ledger.ledger.id}>
                    {ledger.ledger.currencyCode}
                  </option>
                ))}
              </select>
            </div>
          )}
        </div>

        {/* Balance Display */}
        {selectedLedger && (
          <div className={`mt-4 p-4 rounded-lg ${hasInsufficientBalance ? 'bg-[var(--color-warning-soft)] border border-[var(--color-warning)]' : 'bg-[var(--accent-sand-soft)] border border-[var(--accent-sand)]'}`}>
            <div className="flex justify-between items-center">
              <div>
                <p className="text-base text-[var(--text-muted)]">可用餘額</p>
                <p className={`text-xl font-bold number-display ${hasInsufficientBalance ? 'text-[var(--color-warning)]' : 'text-[var(--accent-sand)]'}`}>
                  {effectiveBalance.toLocaleString('zh-TW', { minimumFractionDigits: 2, maximumFractionDigits: 4 })} {selectedLedger.ledger.currencyCode}
                </p>
              </div>
              <div className="text-right">
                <p className="text-base text-[var(--text-muted)]">所需金額</p>
                <p className={`text-xl font-bold number-display ${hasInsufficientBalance ? 'text-[var(--color-warning)]' : 'text-[var(--text-primary)]'}`}>
                  {requiredAmount.toLocaleString('zh-TW', { minimumFractionDigits: 2, maximumFractionDigits: 4 })} {selectedLedger.ledger.currencyCode}
                </p>
              </div>
            </div>
            {hasInsufficientBalance && (
              <p className="text-base text-[var(--color-warning)] mt-3">
                注意：餘額將變為負數
              </p>
            )}
          </div>
        )}
      </div>

      <div>
        <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
          備註（選填）
        </label>
        <textarea
          name="notes"
          value={formData.notes}
          onChange={handleChange}
          rows={2}
          maxLength={500}
          className="input-dark w-full"
          placeholder="輸入備註..."
        />
      </div>

      <div className="flex gap-3">
        <button
          type="submit"
          disabled={isSubmitting}
          className="btn-accent flex-1 py-3 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {isSubmitting ? '處理中...' : (initialData ? '儲存' : '新增交易')}
        </button>
        {onCancel && (
          <button
            type="button"
            onClick={onCancel}
            className="btn-dark px-6 py-3"
          >
            取消
          </button>
        )}
      </div>
    </form>
  );
}
