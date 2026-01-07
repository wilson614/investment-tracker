import { useState, useEffect } from 'react';
import { currencyLedgerApi } from '../../services/api';
import type { CreateStockTransactionRequest, TransactionType, FundSource, CurrencyLedgerSummary } from '../../types';
import { FundSource as FundSourceEnum } from '../../types';

interface TransactionFormProps {
  portfolioId: string;
  onSubmit: (data: CreateStockTransactionRequest) => Promise<void>;
  onCancel?: () => void;
}

export function TransactionForm({ portfolioId, onSubmit, onCancel }: TransactionFormProps) {
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [currencyLedgers, setCurrencyLedgers] = useState<CurrencyLedgerSummary[]>([]);
  const [selectedLedger, setSelectedLedger] = useState<CurrencyLedgerSummary | null>(null);

  const [formData, setFormData] = useState({
    ticker: '',
    transactionType: 1 as TransactionType, // Buy
    transactionDate: new Date().toISOString().split('T')[0],
    shares: '',
    pricePerShare: '',
    exchangeRate: '',
    fees: '0',
    fundSource: FundSourceEnum.None as FundSource,
    currencyLedgerId: '',
    notes: '',
  });

  // Load currency ledgers for fund source selection
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

  const handleChange = (
    e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>
  ) => {
    const { name, value } = e.target;
    setFormData((prev) => ({ ...prev, [name]: value }));
  };

  const handleFundSourceChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const value = Number(e.target.value) as FundSource;
    setFormData((prev) => ({
      ...prev,
      fundSource: value,
      currencyLedgerId: value === FundSourceEnum.None ? '' : prev.currencyLedgerId,
    }));
  };

  // Calculate required amount for display
  const calculateRequiredAmount = () => {
    const shares = parseFloat(formData.shares) || 0;
    const price = parseFloat(formData.pricePerShare) || 0;
    const fees = parseFloat(formData.fees) || 0;
    return (shares * price) + fees;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setIsSubmitting(true);

    try {
      const request: CreateStockTransactionRequest = {
        portfolioId,
        ticker: formData.ticker.toUpperCase(),
        transactionType: Number(formData.transactionType) as TransactionType,
        transactionDate: formData.transactionDate,
        shares: parseFloat(formData.shares),
        pricePerShare: parseFloat(formData.pricePerShare),
        exchangeRate: parseFloat(formData.exchangeRate),
        fees: parseFloat(formData.fees) || 0,
        fundSource: formData.fundSource,
        currencyLedgerId: formData.currencyLedgerId || undefined,
        notes: formData.notes || undefined,
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
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create transaction');
    } finally {
      setIsSubmitting(false);
    }
  };

  const requiredAmount = calculateRequiredAmount();
  const hasInsufficientBalance = selectedLedger && requiredAmount > selectedLedger.balance;

  return (
    <form onSubmit={handleSubmit} className="card-dark space-y-5 p-6">
      <h3 className="text-lg font-bold text-[var(--text-primary)]">新增交易</h3>

      {error && (
        <div className="p-3 bg-[var(--color-danger-soft)] border border-[var(--color-danger)] text-[var(--color-danger)] rounded-lg text-base">{error}</div>
      )}

      <div className="grid grid-cols-2 gap-4">
        <div>
          <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
            股票代號
          </label>
          <input
            type="text"
            name="ticker"
            value={formData.ticker}
            onChange={handleChange}
            required
            maxLength={20}
            className="input-dark w-full"
            placeholder="例如：VWRA"
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
            每股價格
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

        <div>
          <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
            匯率
          </label>
          <input
            type="number"
            name="exchangeRate"
            value={formData.exchangeRate}
            onChange={handleChange}
            required
            min="0.000001"
            step="0.000001"
            className="input-dark w-full"
            placeholder="31.5"
          />
        </div>

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
                <option value="">選擇帳本...</option>
                {currencyLedgers.map((ledger) => (
                  <option key={ledger.ledger.id} value={ledger.ledger.id}>
                    {ledger.ledger.currencyCode} - {ledger.ledger.name}
                  </option>
                ))}
              </select>
            </div>
          )}
        </div>

        {/* Balance Display */}
        {selectedLedger && (
          <div className={`mt-4 p-4 rounded-lg ${hasInsufficientBalance ? 'bg-[var(--color-danger-soft)] border border-[var(--color-danger)]' : 'bg-[var(--accent-sand-soft)] border border-[var(--accent-sand)]'}`}>
            <div className="flex justify-between items-center">
              <div>
                <p className="text-base text-[var(--text-muted)]">可用餘額</p>
                <p className={`text-xl font-bold number-display ${hasInsufficientBalance ? 'text-[var(--color-danger)]' : 'text-[var(--accent-sand)]'}`}>
                  {selectedLedger.balance.toLocaleString('zh-TW', { minimumFractionDigits: 2, maximumFractionDigits: 4 })} {selectedLedger.ledger.currencyCode}
                </p>
              </div>
              <div className="text-right">
                <p className="text-base text-[var(--text-muted)]">所需金額</p>
                <p className={`text-xl font-bold number-display ${hasInsufficientBalance ? 'text-[var(--color-danger)]' : 'text-[var(--text-primary)]'}`}>
                  {requiredAmount.toLocaleString('zh-TW', { minimumFractionDigits: 2, maximumFractionDigits: 4 })} {selectedLedger.ledger.currencyCode}
                </p>
              </div>
            </div>
            {hasInsufficientBalance && (
              <p className="text-base text-[var(--color-danger)] mt-3">
                餘額不足。請增加資金或選擇其他資金來源。
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
          disabled={isSubmitting || (hasInsufficientBalance ?? false)}
          className="btn-accent flex-1 py-3 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {isSubmitting ? '新增中...' : '新增交易'}
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
