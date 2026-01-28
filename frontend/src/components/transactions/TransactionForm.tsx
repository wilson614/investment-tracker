/**
 * TransactionForm
 *
 * Stock transaction add/edit form: supports buy/sell/split/adjustment.
 * Automatically uses portfolio's bound ledger for linked transactions.
 *
 * Key behaviors:
 * - Taiwan stocks auto-set exchange rate to 1
 * - All buy/sell transactions auto-link to bound ledger
 * - Exchange rate is optional - backend will fetch from transaction date if not provided
 */
import { useState, useEffect, useCallback, useRef } from 'react';
import { Loader2, Info } from 'lucide-react';
import { currencyLedgerApi, stockPriceApi } from '../../services/api';
import type { CreateStockTransactionRequest, StockTransaction, TransactionType, CurrencyLedgerSummary, StockMarket, Currency, Portfolio } from '../../types';
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

export function TransactionForm({ portfolioId, portfolio, initialData, onSubmit, onCancel }: TransactionFormProps) {
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [boundLedger, setBoundLedger] = useState<CurrencyLedgerSummary | null>(null);
  const [isDetectingMarket, setIsDetectingMarket] = useState(false);
  const [userSelectedMarket, setUserSelectedMarket] = useState(false);
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

  const handleChange = (
    e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>
  ) => {
    const { name, value } = e.target;
    setFormData((prev) => {
      const newData = { ...prev, [name]: value };
      if (name === 'ticker' && !userSelectedMarket) {
        const newMarket = guessMarketFromTicker(value);
        newData.market = newMarket;
        newData.currency = guessCurrencyFromMarket(newMarket);
        newData.exchangeRate = newMarket === StockMarketEnum.TW ? '1' : '';
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

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setIsSubmitting(true);

    try {
      let exchangeRateValue: number | undefined;
      if (isTW) {
        exchangeRateValue = 1;
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

  // Calculate required amount for display
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
  const hasInsufficientBalance = boundLedger && requiredAmount > effectiveBalance && Number(formData.transactionType) === 1;

  return (
    <form onSubmit={handleSubmit} className="card-dark space-y-5 p-6">
      <h3 className="text-lg font-bold text-[var(--text-primary)]">{initialData ? 'Edit Transaction' : 'Add Transaction'}</h3>

      {error && (
        <div className="p-3 bg-[var(--color-danger-soft)] border border-[var(--color-danger)] text-[var(--color-danger)] rounded-lg text-base">{error}</div>
      )}

      <div className="grid grid-cols-2 gap-4">
        <div>
          <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
            Ticker
            {isTW && <span className="ml-2 text-xs text-[var(--accent-peach)]">Taiwan Stock</span>}
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
            Transaction Type
          </label>
          <select
            name="transactionType"
            value={formData.transactionType}
            onChange={handleChange}
            className="input-dark w-full"
          >
            <option value={1}>Buy</option>
            <option value={2}>Sell</option>
          </select>
        </div>

        <div>
          <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
            Market
            {isDetectingMarket && (
              <span className="ml-2 text-xs text-[var(--text-muted)] inline-flex items-center gap-1">
                <Loader2 className="w-3 h-3 animate-spin" />
                Detecting...
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
                exchangeRate: newMarket === StockMarketEnum.TW ? '1' : '',
              }));
              setUserSelectedMarket(true);
            }}
            className="input-dark w-full"
            disabled={isDetectingMarket}
          >
            <option value={StockMarketEnum.TW}>Taiwan</option>
            <option value={StockMarketEnum.US}>US</option>
            <option value={StockMarketEnum.UK}>UK</option>
            <option value={StockMarketEnum.EU}>EU</option>
          </select>
        </div>

        {!isTW && (
          <div>
            <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
              Currency
            </label>
            <select
              name="currency"
              value={formData.currency}
              onChange={(e) => {
                const newCurrency = Number(e.target.value) as Currency;
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
            Date
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
            Shares
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
            Price
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

        {!isTW && (
          <div>
            <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
              Exchange Rate (Optional)
              <span className="ml-2 text-xs text-[var(--text-muted)]">Auto-fetch if empty</span>
            </label>
            <input
              type="number"
              name="exchangeRate"
              value={formData.exchangeRate}
              onChange={handleChange}
              min="0.000001"
              step="0.000001"
              className="input-dark w-full"
              placeholder="Leave empty for auto"
            />
          </div>
        )}

        <div>
          <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
            Fees
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

      {/* Bound Ledger Info */}
      {boundLedger && (
        <div className="border-t border-[var(--border-color)] pt-5 mt-5">
          <h4 className="text-base font-medium text-[var(--text-secondary)] mb-4 flex items-center gap-2">
            <Info className="w-4 h-4" />
            Linked Ledger
          </h4>
          <div className="p-3 bg-[var(--accent-cyan-soft)] border border-[var(--accent-cyan)] rounded-lg text-[var(--accent-cyan)] text-sm font-medium">
            {Number(formData.transactionType) === 1
              ? `This transaction will deduct from ${boundLedger.ledger.currencyCode} ledger`
              : `Proceeds will be credited to ${boundLedger.ledger.currencyCode} ledger`}
          </div>

          <div className={`mt-4 p-4 rounded-lg ${hasInsufficientBalance ? 'bg-[var(--color-warning-soft)] border border-[var(--color-warning)]' : 'bg-[var(--accent-sand-soft)] border border-[var(--accent-sand)]'}`}>
            <div className="flex justify-between items-center">
              <div>
                <p className="text-base text-[var(--text-muted)]">Available Balance</p>
                <p className={`text-xl font-bold number-display ${hasInsufficientBalance ? 'text-[var(--color-warning)]' : 'text-[var(--accent-sand)]'}`}>
                  {effectiveBalance.toLocaleString('zh-TW', { minimumFractionDigits: 2, maximumFractionDigits: 4 })} {boundLedger.ledger.currencyCode}
                </p>
              </div>
              <div className="text-right">
                <p className="text-base text-[var(--text-muted)]">Required Amount</p>
                <p className={`text-xl font-bold number-display ${hasInsufficientBalance ? 'text-[var(--color-warning)]' : 'text-[var(--text-primary)]'}`}>
                  {requiredAmount.toLocaleString('zh-TW', { minimumFractionDigits: 2, maximumFractionDigits: 4 })} {boundLedger.ledger.currencyCode}
                </p>
              </div>
            </div>
            {hasInsufficientBalance && (
              <p className="text-base text-[var(--color-warning)] mt-3">
                Note: Balance will become negative
              </p>
            )}
          </div>
        </div>
      )}

      <div>
        <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
          Notes (Optional)
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
          {isSubmitting ? 'Processing...' : (initialData ? 'Save' : 'Add Transaction')}
        </button>
        {onCancel && (
          <button
            type="button"
            onClick={onCancel}
            className="btn-dark px-6 py-3"
          >
            Cancel
          </button>
        )}
      </div>
    </form>
  );
}
