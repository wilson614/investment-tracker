/**
 * Currency Page
 *
 * 外幣帳本首頁：列出使用者的外幣帳本摘要，並提供建立新帳本的入口。
 *
 * 目前限制：
 * - `SUPPORTED_CURRENCIES` 目前僅包含 USD（後續可擴充）。
 */
import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { currencyLedgerApi } from '../services/api';
import { CurrencyLedgerCard } from '../components/currency/CurrencyLedgerCard';
import type { CurrencyLedgerSummary, CreateCurrencyLedgerRequest } from '../types';

/**
 * 可建立的幣別清單。
 *
 * 目前僅提供 USD，未來可視需求擴充。
 */
const SUPPORTED_CURRENCIES = [
  { code: 'USD', name: '美金' },
];

export default function Currency() {
  const navigate = useNavigate();
  const [ledgers, setLedgers] = useState<CurrencyLedgerSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showCreateForm, setShowCreateForm] = useState(false);

  // Create form state - 預設選擇第一個可用的幣別
  const [selectedCurrency, setSelectedCurrency] = useState('USD');
  const [isSubmitting, setIsSubmitting] = useState(false);

  /**
   * 載入所有外幣帳本摘要。
   */
  const loadLedgers = async () => {
    try {
      setLoading(true);
      const data = await currencyLedgerApi.getAll();
      setLedgers(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load currency ledgers');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadLedgers();
  }, []);

  // Get existing currency codes
  const existingCurrencies = new Set(ledgers.map(l => l.ledger.currencyCode));

  /**
   * 建立新的外幣帳本。
   * @param e React 表單事件
   */
  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedCurrency) return;
    
    setIsSubmitting(true);
    try {
      const currency = SUPPORTED_CURRENCIES.find(c => c.code === selectedCurrency);
      const request: CreateCurrencyLedgerRequest = {
        currencyCode: selectedCurrency,
        name: currency?.name || selectedCurrency,
      };
      await currencyLedgerApi.create(request);
      setSelectedCurrency('');
      setShowCreateForm(false);
      await loadLedgers();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create ledger');
    } finally {
      setIsSubmitting(false);
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-[var(--text-muted)] text-lg">載入中...</div>
      </div>
    );
  }

  // 判斷是否還有可建立的幣別：若全部幣別都已建立，則禁用「新增帳本」。
  const availableCurrencies = SUPPORTED_CURRENCIES.filter(c => !existingCurrencies.has(c.code));
  const canCreateNew = availableCurrencies.length > 0;

  return (
    <div className="min-h-screen py-8">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="flex justify-between items-center mb-8">
          <h1 className="text-2xl font-bold text-[var(--text-primary)]">外幣帳本</h1>
          <button
            onClick={() => setShowCreateForm(true)}
            disabled={!canCreateNew}
            className="btn-accent disabled:opacity-50 disabled:cursor-not-allowed"
            title={!canCreateNew ? '所有幣別已建立' : undefined}
          >
            新增帳本
          </button>
        </div>

        {error && (
          <div className="bg-[var(--color-danger-soft)] border border-[var(--color-danger)] text-[var(--color-danger)] p-4 rounded-lg mb-6 flex justify-between items-center">
            <span className="text-base">{error}</span>
            <button onClick={() => setError(null)} className="hover:underline text-base">關閉</button>
          </div>
        )}

        {/* Create Form Modal */}
        {showCreateForm && (
          <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50">
            <div className="card-dark p-6 w-full max-w-md m-4">
              <h2 className="text-xl font-bold text-[var(--text-primary)] mb-6">新增外幣帳本</h2>
              <form onSubmit={handleCreate} className="space-y-5">
                <div>
                  <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                    幣別
                  </label>
                  <select
                    value={selectedCurrency}
                    onChange={(e) => setSelectedCurrency(e.target.value)}
                    className="input-dark w-full"
                    required
                  >
                    {SUPPORTED_CURRENCIES.map((currency) => {
                      const isDisabled = existingCurrencies.has(currency.code);
                      return (
                        <option
                          key={currency.code}
                          value={currency.code}
                          disabled={isDisabled}
                        >
                          {currency.code} - {currency.name}{isDisabled ? ' (已建立)' : ''}
                        </option>
                      );
                    })}
                  </select>
                </div>
                <div className="flex gap-3">
                  <button
                    type="button"
                    onClick={() => setShowCreateForm(false)}
                    className="btn-dark flex-1 py-2"
                  >
                    取消
                  </button>
                  <button
                    type="submit"
                    disabled={isSubmitting || !selectedCurrency}
                    className="btn-accent flex-1 py-2 disabled:opacity-50"
                  >
                    {isSubmitting ? '建立中...' : '建立'}
                  </button>
                </div>
              </form>
            </div>
          </div>
        )}

        {/* Ledger List */}
        {ledgers.length === 0 ? (
          <div className="card-dark p-12 text-center">
            <p className="text-[var(--text-muted)] text-lg">尚無外幣帳本</p>
            <p className="text-base text-[var(--text-muted)] mt-2">點擊「新增帳本」開始追蹤您的外幣資產</p>
          </div>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {ledgers.map((ledger) => (
              <CurrencyLedgerCard
                key={ledger.ledger.id}
                ledger={ledger}
                onClick={() => navigate(`/currency/${ledger.ledger.id}`)}
              />
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
