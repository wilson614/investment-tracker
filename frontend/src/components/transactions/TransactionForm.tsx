/**
 * TransactionForm
 *
 * 股票交易新增/編輯表單：支援買入/賣出/分割/調整，並可選擇資金來源（例如外幣帳本）。
 *
 * 重要行為：
 * - 台股會自動將匯率補為 1（若使用者未填）。
 * - 若資金來源為 CurrencyLedger 且不是台股，則匯率欄位可省略，交由 backend 依帳本推導。
 * - 若資金來源為外幣帳本，則匯率欄位可省略。
 */
import { useState, useEffect, useCallback, useRef } from 'react';
import { Loader2 } from 'lucide-react';
import { currencyLedgerApi, stockPriceApi } from '../../services/api';
import type { CreateStockTransactionRequest, StockTransaction, TransactionType, FundSource, CurrencyLedgerSummary, StockMarket, Currency, Portfolio } from '../../types';
import { FundSource as FundSourceEnum, StockMarket as StockMarketEnum, Currency as CurrencyEnum } from '../../types';

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

/**
 * 根據 market 推測 currency
 * TW → TWD，其他 → USD（使用者可手動覆寫為 GBP/EUR）
 */
const guessCurrencyFromMarket = (market: StockMarket): Currency => {
  return market === StockMarketEnum.TW ? CurrencyEnum.TWD : CurrencyEnum.USD;
};

interface TransactionFormProps {
  /** 目標 portfolio ID */
  portfolioId: string;
  /** 投資組合資訊 (用來判斷 TWD Ledger 綁定) */
  portfolio?: Portfolio | null;
  /** 編輯模式的初始資料 */
  initialData?: StockTransaction;
  /** 送出表單 callback */
  onSubmit: (data: CreateStockTransactionRequest) => Promise<void>;
  /** 取消 callback */
  onCancel?: () => void;
}

export function TransactionForm({ portfolioId, portfolio, initialData, onSubmit, onCancel }: TransactionFormProps) {
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [currencyLedgers, setCurrencyLedgers] = useState<CurrencyLedgerSummary[]>([]);
  const [selectedLedger, setSelectedLedger] = useState<CurrencyLedgerSummary | null>(null);
  const [isDetectingMarket, setIsDetectingMarket] = useState(false);
  // 追蹤使用者是否手動選擇了 market（避免自動偵測覆蓋使用者選擇）
  const [userSelectedMarket, setUserSelectedMarket] = useState(false);
  // debounce timer ref
  const detectMarketTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const [formData, setFormData] = useState(() => {
    const initialMarket = initialData?.market ?? guessMarketFromTicker(initialData?.ticker ?? '');
    return {
      ticker: initialData?.ticker ?? '',
      transactionType: (initialData?.transactionType ?? 1) as TransactionType,
      transactionDate: initialData?.transactionDate?.split('T')[0] ?? new Date().toISOString().split('T')[0],
      shares: initialData?.shares?.toString() ?? '',
      pricePerShare: initialData?.pricePerShare?.toString() ?? '',
      exchangeRate: initialData?.exchangeRate?.toString() ?? '',
      fees: initialData?.fees?.toString() ?? '',
      fundSource: (initialData?.fundSource ?? FundSourceEnum.None) as FundSource,
      currencyLedgerId: initialData?.currencyLedgerId ?? '',
      notes: initialData?.notes ?? '',
      market: initialMarket as StockMarket,
      currency: (initialData?.currency ?? guessCurrencyFromMarket(initialMarket)) as Currency,
    };
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

  // Derived state: is current market Taiwan?
  // 匯率框隱藏邏輯跟著市場選擇，而非 ticker 格式
  const isTW = formData.market === StockMarketEnum.TW;

  // Derived state: TWD Ledger 綁定狀態
  // 當 Portfolio 有綁定 TWD Ledger 且當前為台股時，強制連結
  const isTwBound = !!(portfolio?.boundCurrencyLedgerId && isTW);

  // Effect: 當符合 TWD Ledger 綁定條件時，自動設定資金來源
  useEffect(() => {
    if (isTwBound && portfolio?.boundCurrencyLedgerId) {
      setFormData(prev => {
        // 如果已經是正確狀態，則不更新避免無窮迴圈
        if (prev.fundSource === FundSourceEnum.CurrencyLedger && prev.currencyLedgerId === portfolio.boundCurrencyLedgerId) {
          return prev;
        }
        return {
          ...prev,
          fundSource: FundSourceEnum.CurrencyLedger,
          currencyLedgerId: portfolio.boundCurrencyLedgerId!
        };
      });
    }
  }, [isTwBound, portfolio?.boundCurrencyLedgerId]);

  // Derived state: is using currency ledger for non-TW stock?
  const useCurrencyLedger = formData.fundSource === FundSourceEnum.CurrencyLedger && !isTW;

  /**
   * 自動偵測市場：先嘗試 US，找不到再嘗試 UK。
   * 若兩者都有，則預設 US（使用者可手動切換）。
   */
  const detectMarket = useCallback(async (ticker: string): Promise<StockMarket | null> => {
    if (!ticker.trim()) return null;
    const trimmed = ticker.trim().toUpperCase();

    // 台股：數字開頭，不需要查詢 API
    if (/^\d/.test(trimmed)) return StockMarketEnum.TW;

    // .L 結尾直接判定為英股
    if (trimmed.endsWith('.L')) return StockMarketEnum.UK;

    // 對於其他 ticker，先嘗試查詢 US 市場
    try {
      const usQuote = await stockPriceApi.getQuote(StockMarketEnum.US, trimmed);
      if (usQuote) {
        // US 市場有此 ticker
        return StockMarketEnum.US;
      }
    } catch {
      // US 查詢失敗，繼續嘗試 UK
    }

    // 嘗試 UK 市場
    try {
      const ukQuote = await stockPriceApi.getQuote(StockMarketEnum.UK, trimmed);
      if (ukQuote) {
        return StockMarketEnum.UK;
      }
    } catch {
      // UK 也找不到
    }

    // 都找不到，預設 US（讓使用者手動選擇）
    return StockMarketEnum.US;
  }, []);

  /**
   * 觸發市場偵測（帶 debounce）。
   * 只有當 ticker 長度 >= 4 且使用者未手動選擇 market 時才觸發。
   */
  const triggerMarketDetection = useCallback((ticker: string) => {
    // 清除之前的 timer
    if (detectMarketTimerRef.current) {
      clearTimeout(detectMarketTimerRef.current);
      detectMarketTimerRef.current = null;
    }

    const trimmed = ticker.trim();
    // 少於 4 字元不觸發
    if (trimmed.length < 4) return;
    // 台股不需要 API 偵測
    if (isTaiwanStock(trimmed)) return;
    // 使用者已手動選擇 market，不覆蓋
    if (userSelectedMarket) return;

    // debounce 300ms
    detectMarketTimerRef.current = setTimeout(async () => {
      setIsDetectingMarket(true);
      try {
        const detectedMarket = await detectMarket(trimmed);
        if (detectedMarket !== null) {
          setFormData((prev) => ({
            ...prev,
            market: detectedMarket,
            currency: guessCurrencyFromMarket(detectedMarket),
          }));
        }
      } finally {
        setIsDetectingMarket(false);
      }
    }, 300);
  }, [detectMarket, userSelectedMarket]);

  // 清理 timer
  useEffect(() => {
    return () => {
      if (detectMarketTimerRef.current) {
        clearTimeout(detectMarketTimerRef.current);
      }
    };
  }, []);

  /**
   * 表單欄位更新。
   *
   * 特殊規則：當市場為台股時，匯率固定為 1。
   */
  const handleChange = (
    e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>
  ) => {
    const { name, value } = e.target;
    setFormData((prev) => {
      const newData = { ...prev, [name]: value };
      // Auto-set market based on ticker (quick guess, not API call)
      if (name === 'ticker' && !userSelectedMarket) {
        const newMarket = guessMarketFromTicker(value);
        newData.market = newMarket;
        newData.currency = guessCurrencyFromMarket(newMarket);
        // 台股匯率固定為 1，其他市場清空
        newData.exchangeRate = newMarket === StockMarketEnum.TW ? '1' : '';
      }
      // Auto-set currency when market changes
      if (name === 'market') {
        newData.currency = guessCurrencyFromMarket(Number(value) as StockMarket);
        // 使用者手動選擇了 market
        setUserSelectedMarket(true);
      }
      return newData;
    });

    // 當 ticker 輸入時，觸發市場偵測（在 4+ 字元時）
    if (name === 'ticker') {
      triggerMarketDetection(value);
    }
  };

  // Handle ticker blur - market detection now happens on 4th char, no special handling needed
  const handleTickerBlur = () => {
    // No-op: 匯率處理已經跟著市場選擇，不需要在 blur 時額外處理
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
      const shouldOmitExchangeRate = formData.fundSource === FundSourceEnum.CurrencyLedger && !isTW;

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
        currency: formData.currency,
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
        fees: '',
        fundSource: FundSourceEnum.None as FundSource,
        currencyLedgerId: '',
        notes: '',
        market: StockMarketEnum.US as StockMarket,
        currency: CurrencyEnum.USD as Currency,
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
            {isDetectingMarket && (
              <span className="ml-2 text-xs text-[var(--text-muted)] inline-flex items-center gap-1">
                <Loader2 className="w-3 h-3 animate-spin" />
                偵測中...
              </span>
            )}
          </label>
          <select
            name="market"
            value={formData.market}
            onChange={(e) => {
              const newMarket = Number(e.target.value) as StockMarket;
              const newCurrency = guessCurrencyFromMarket(newMarket);
              setFormData(prev => ({
                ...prev,
                market: newMarket,
                currency: newCurrency,
                // 台股匯率固定為 1，其他市場清空讓後端自動抓取
                exchangeRate: newMarket === StockMarketEnum.TW ? '1' : '',
              }));
              // 使用者手動選擇了 market
              setUserSelectedMarket(true);
            }}
            className="input-dark w-full"
            disabled={isDetectingMarket}
          >
            <option value={StockMarketEnum.TW}>台股</option>
            <option value={StockMarketEnum.US}>美股</option>
            <option value={StockMarketEnum.UK}>英股</option>
            <option value={StockMarketEnum.EU}>歐股</option>
          </select>
        </div>

        {/* 計價幣別 - 台股時隱藏（固定為 TWD） */}
        {!isTW && (
          <div>
            <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
              計價幣別
            </label>
            <select
              name="currency"
              value={formData.currency}
              onChange={(e) => {
                const newCurrency = Number(e.target.value) as Currency;
                // 幣別變更時清空匯率，讓後端自動抓取
                setFormData(prev => ({
                  ...prev,
                  currency: newCurrency,
                  exchangeRate: newCurrency === CurrencyEnum.TWD ? '1' : '',
                }));
              }}
              className="input-dark w-full"
            >
              <option value={CurrencyEnum.TWD}>TWD</option>
              <option value={CurrencyEnum.USD}>USD</option>
              <option value={CurrencyEnum.GBP}>GBP</option>
              <option value={CurrencyEnum.EUR}>EUR</option>
            </select>
          </div>
        )}

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
          />
        </div>

        {/* Exchange rate - hidden for Taiwan stocks and currency ledger */}
        {!useCurrencyLedger && !isTW && (
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
          />
        </div>
      </div>

      {/* Fund Source Section */}
      <div className="border-t border-[var(--border-color)] pt-5 mt-5">
        <h4 className="text-base font-medium text-[var(--text-secondary)] mb-4">資金來源</h4>

        {isTwBound ? (
          <div className="space-y-4">
            <div className="p-3 bg-[var(--accent-cyan-soft)] border border-[var(--accent-cyan)] rounded-lg text-[var(--accent-cyan)] text-sm font-medium">
              {Number(formData.transactionType) === 1
                ? '此筆交易將自動從 TWD 帳本扣款'
                : '此筆交易款項將自動存入 TWD 帳本'}
            </div>
            <div>
              <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                已連結帳本
              </label>
              <input
                type="text"
                value={selectedLedger?.ledger.name || selectedLedger?.ledger.currencyCode || 'Loading...'}
                disabled
                className="input-dark w-full opacity-75 cursor-not-allowed"
              />
            </div>
          </div>
        ) : (
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
        )}

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
