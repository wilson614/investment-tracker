/**
 * TransactionForm
 *
 * Stock transaction add/edit form: supports buy/sell/split/adjustment.
 * Automatically uses portfolio's bound ledger for linked transactions.
 *
 * Key behaviors:
 * - Buy/sell transactions auto-link to bound ledger
 * - Foreign-currency buy shows system-calculated exchange-rate preview
 */
import { useState, useEffect, useCallback, useRef } from 'react';
import { Loader2 } from 'lucide-react';
import { currencyLedgerApi, stockPriceApi } from '../../services/api';
import { ConfirmationModal } from '../modals/ConfirmationModal';
import type { CreateStockTransactionRequest, StockTransaction, TransactionType, CurrencyLedgerSummary, StockMarket, Currency, Portfolio, ExchangeRatePreviewResponse } from '../../types';
import { StockMarket as StockMarketEnum, Currency as CurrencyEnum } from '../../types';

/**
 * Check if ticker is Taiwan stock (starts with digit)
 */
const isTaiwanStock = (ticker: string): boolean => {
  return /^\d+[A-Za-z]*$/.test(ticker.trim());
};

/**
 * Guess market from ticker
 */
const guessMarketFromTicker = (ticker: string): StockMarket => {
  if (!ticker) return StockMarketEnum.US;
  const trimmed = ticker.trim().toUpperCase();
  if (/^\d/.test(trimmed)) return StockMarketEnum.TW;
  if (trimmed.endsWith('.L')) return StockMarketEnum.UK;
  return StockMarketEnum.US;
};

/**
 * Guess currency from market
 */
const guessCurrencyFromMarket = (market: StockMarket): Currency => {
  return market === StockMarketEnum.TW ? CurrencyEnum.TWD : CurrencyEnum.USD;
};

interface TransactionFormProps {
  portfolioId: string;
  portfolio?: Portfolio | null;
  initialData?: StockTransaction;
  onSubmit: (data: CreateStockTransactionRequest) => Promise<void>;
  onCancel?: () => void;
}

import { getErrorMessage } from '../../utils/errorMapping';

export function TransactionForm({ portfolioId, portfolio, initialData, onSubmit, onCancel }: TransactionFormProps) {
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [boundLedger, setBoundLedger] = useState<CurrencyLedgerSummary | null>(null);
  const [isDetectingMarket, setIsDetectingMarket] = useState(false);
  const [userSelectedMarket, setUserSelectedMarket] = useState(false);
  const detectMarketTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Modal state
  const [showAutoDepositModal, setShowAutoDepositModal] = useState(false);
  const [insufficientAmount, setInsufficientAmount] = useState<number>(0);

  const [exchangeRatePreview, setExchangeRatePreview] = useState<ExchangeRatePreviewResponse | null>(null);
  const [isLoadingRate, setIsLoadingRate] = useState(false);
  const [rateError, setRateError] = useState<string | null>(null);

  const [formData, setFormData] = useState(() => {
    const initialMarket = initialData?.market ?? guessMarketFromTicker(initialData?.ticker ?? '');
    return {
      ticker: initialData?.ticker ?? '',
      transactionType: (initialData?.transactionType ?? 1) as TransactionType,
      transactionDate: initialData?.transactionDate?.split('T')[0] ?? new Date().toISOString().split('T')[0],
      shares: initialData?.shares?.toString() ?? '',
      pricePerShare: initialData?.pricePerShare?.toString() ?? '',
      fees: initialData?.fees?.toString() ?? '',
      notes: initialData?.notes ?? '',
      market: initialMarket as StockMarket,
      currency: (initialData?.currency ?? guessCurrencyFromMarket(initialMarket)) as Currency,
    };
  });

  // Load bound ledger info
  useEffect(() => {
    const loadBoundLedger = async () => {
      if (!portfolio?.boundCurrencyLedgerId) return;
      try {
        const ledgers = await currencyLedgerApi.getAll();
        const bound = ledgers.find(l => l.ledger.id === portfolio.boundCurrencyLedgerId);
        setBoundLedger(bound || null);
      } catch {
        console.error('Failed to load bound ledger');
      }
    };
    loadBoundLedger();
  }, [portfolio?.boundCurrencyLedgerId]);

  const isTW = formData.market === StockMarketEnum.TW;

  /**
   * Auto-detect market from ticker
   */
  const detectMarket = useCallback(async (ticker: string): Promise<StockMarket | null> => {
    if (!ticker.trim()) return null;
    const trimmed = ticker.trim().toUpperCase();

    if (/^\d/.test(trimmed)) return StockMarketEnum.TW;
    if (trimmed.endsWith('.L')) return StockMarketEnum.UK;

    try {
      const usQuote = await stockPriceApi.getQuote(StockMarketEnum.US, trimmed);
      if (usQuote) return StockMarketEnum.US;
    } catch {
      // Continue to UK
    }

    try {
      const ukQuote = await stockPriceApi.getQuote(StockMarketEnum.UK, trimmed);
      if (ukQuote) return StockMarketEnum.UK;
    } catch {
      // Not found
    }

    return StockMarketEnum.US;
  }, []);

  const triggerMarketDetection = useCallback((ticker: string) => {
    if (detectMarketTimerRef.current) {
      clearTimeout(detectMarketTimerRef.current);
      detectMarketTimerRef.current = null;
    }

    const trimmed = ticker.trim();
    if (trimmed.length < 4) return;
    if (isTaiwanStock(trimmed)) return;
    if (userSelectedMarket) return;

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

  useEffect(() => {
    return () => {
      if (detectMarketTimerRef.current) {
        clearTimeout(detectMarketTimerRef.current);
      }
    };
  }, []);

  useEffect(() => {
    const isBuy = Number(formData.transactionType) === 1;
    const isNonTwdCurrency = Number(formData.currency) !== CurrencyEnum.TWD;
    const hasBoundLedger = Boolean(portfolio?.boundCurrencyLedgerId);
    const shares = parseFloat(formData.shares);
    const pricePerShare = parseFloat(formData.pricePerShare);
    const hasValidAmountInputs = Number.isFinite(shares) && shares > 0 && Number.isFinite(pricePerShare) && pricePerShare > 0;

    if (!isBuy || !isNonTwdCurrency || !hasBoundLedger || !hasValidAmountInputs) {
      setExchangeRatePreview(null);
      setRateError(null);
      setIsLoadingRate(false);
      return;
    }

    const fees = parseFloat(formData.fees) || 0;
    const amount = shares * pricePerShare + fees;
    const ledgerId = portfolio!.boundCurrencyLedgerId;

    let isCancelled = false;
    const timer = setTimeout(() => {
      setIsLoadingRate(true);
      setRateError(null);

      void currencyLedgerApi.getExchangeRatePreview(ledgerId, amount, formData.transactionDate)
        .then((result) => {
          if (isCancelled) return;
          setExchangeRatePreview(result);
          setIsLoadingRate(false);
        })
        .catch(() => {
          if (isCancelled) return;
          setRateError('無法取得匯率，請先在帳本中建立換匯紀錄');
          setExchangeRatePreview(null);
          setIsLoadingRate(false);
        });
    }, 300);

    return () => {
      isCancelled = true;
      clearTimeout(timer);
    };
  }, [
    formData.shares,
    formData.pricePerShare,
    formData.transactionDate,
    formData.transactionType,
    formData.currency,
    formData.market,
    formData.fees,
    portfolio?.boundCurrencyLedgerId,
  ]);

  const handleChange = (
    e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>
  ) => {
    const { name, value } = e.target;
    setFormData((prev) => {
      const newData = { ...prev, [name]: value };
      if (name === 'ticker' && !userSelectedMarket) {
        const newMarket = guessMarketFromTicker(value);
        newData.market = newMarket;
        newData.currency = guessCurrencyFromMarket(newMarket);
      }
      if (name === 'market') {
        newData.currency = guessCurrencyFromMarket(Number(value) as StockMarket);
        setUserSelectedMarket(true);
      }
      return newData;
    });

    if (name === 'ticker') {
      triggerMarketDetection(value);
    }
  };

  const executeSubmit = async (autoDeposit: boolean) => {
    setError(null);
    setIsSubmitting(true);

    try {
      const request: CreateStockTransactionRequest & { autoDeposit?: boolean } = {
        portfolioId,
        ticker: formData.ticker.toUpperCase(),
        transactionType: Number(formData.transactionType) as TransactionType,
        transactionDate: formData.transactionDate,
        shares: parseFloat(formData.shares),
        pricePerShare: parseFloat(formData.pricePerShare),
        fees: parseFloat(formData.fees) || 0,
        autoDeposit: autoDeposit ? true : undefined,
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
        fees: '',
        notes: '',
        market: StockMarketEnum.US as StockMarket,
        currency: CurrencyEnum.USD as Currency,
      });
      setExchangeRatePreview(null);
      setRateError(null);
    } catch (err) {
      setError(getErrorMessage(err instanceof Error ? err.message : 'Failed to create transaction'));
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    const requiredAmount = (() => {
      const shares = parseFloat(formData.shares) || 0;
      const price = parseFloat(formData.pricePerShare) || 0;
      const fees = parseFloat(formData.fees) || 0;
      return (shares * price) + fees;
    })();
    const originalAmount = initialData
      ? (initialData.shares * initialData.pricePerShare + initialData.fees)
      : 0;
    const effectiveBalance = boundLedger
      ? boundLedger.balance + originalAmount
      : 0;
    const hasInsufficientBalance = boundLedger
      && Number(formData.transactionType) === 1
      && requiredAmount > effectiveBalance;

    if (hasInsufficientBalance) {
      setInsufficientAmount(requiredAmount - effectiveBalance);
      setShowAutoDepositModal(true);
      return;
    }

    await executeSubmit(false);
  };

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
              }));
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

        {!isTW && (
          <div>
            <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
              幣別
            </label>
            <select
              name="currency"
              value={formData.currency}
              onChange={(e) => {
                const newCurrency = Number(e.target.value) as Currency;
                setFormData(prev => ({
                  ...prev,
                  currency: newCurrency,
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
            交易日期
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

        {!isTW && Number(formData.transactionType) === 1 && (
          <div>
            <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
              匯率
            </label>
            {isLoadingRate ? (
              <div className="flex items-center gap-2 text-[var(--text-muted)] text-sm">
                <Loader2 className="w-4 h-4 animate-spin" />
                <span>計算中...</span>
              </div>
            ) : rateError ? (
              <p className="text-[var(--color-danger)] text-sm">{rateError}</p>
            ) : exchangeRatePreview ? (
              <div className="flex items-center gap-2">
                <span className="text-lg font-semibold text-[var(--text-primary)]">
                  {exchangeRatePreview.rate.toFixed(4)}
                </span>
                <span className="text-xs text-[var(--text-muted)]">
                  {exchangeRatePreview.source === 'lifo'
                    ? '帳本成本'
                    : exchangeRatePreview.source === 'market'
                      ? '市場匯率'
                      : '混合匯率'}
                </span>
              </div>
            ) : null}
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

      <div>
        <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
          備註
        </label>
        <input
          type="text"
          name="notes"
          value={formData.notes}
          onChange={handleChange}
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

      {/* Auto Deposit Confirmation Modal */}
      <ConfirmationModal
        isOpen={showAutoDepositModal}
        onClose={() => setShowAutoDepositModal(false)}
        onConfirm={() => executeSubmit(true)}
        onCancel={() => executeSubmit(false)}
        title="帳本餘額不足"
        message={
          <>
            <p className="mb-2">
              帳本餘額不足（差額 {insufficientAmount.toLocaleString('zh-TW', { maximumFractionDigits: 4 })} {boundLedger?.ledger.currencyCode}）。
            </p>
            <p>
              按「自動補足」＝ 自動建立入金補足差額並繼續。
              <br />
              按「直接繼續」＝ 允許餘額為負並繼續。
            </p>
          </>
        }
        confirmText="自動補足"
        cancelText="直接繼續"
      />
    </form>
  );
}
