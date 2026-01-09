import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { currencyLedgerApi } from '../services/api';
import { CurrencyLedgerCard } from '../components/currency/CurrencyLedgerCard';
import type { CurrencyLedgerSummary, CreateCurrencyLedgerRequest } from '../types';

export default function Currency() {
  const navigate = useNavigate();
  const [ledgers, setLedgers] = useState<CurrencyLedgerSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showCreateForm, setShowCreateForm] = useState(false);

  // Create form state
  const [currencyCode, setCurrencyCode] = useState('');
  const [name, setName] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

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

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSubmitting(true);
    try {
      const request: CreateCurrencyLedgerRequest = {
        currencyCode: currencyCode.toUpperCase(),
        name,
      };
      await currencyLedgerApi.create(request);
      setCurrencyCode('');
      setName('');
      setShowCreateForm(false);
      await loadLedgers();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create ledger');
    } finally {
      setIsSubmitting(false);
    }
  };

  const formatNumber = (value: number | null | undefined) => {
    if (value == null) return '0';
    return value.toLocaleString('zh-TW', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    });
  };

  // Format TWD as integer
  const formatTWD = (value: number | null | undefined) => {
    if (value == null) return '0';
    return Math.round(value).toLocaleString('zh-TW');
  };

  const totalExchanged = ledgers.reduce((sum, l) => sum + l.totalExchanged, 0);
  const totalCost = ledgers.reduce((sum, l) => sum + l.totalCost, 0);
  const totalRealizedPnl = ledgers.reduce((sum, l) => sum + l.realizedPnl, 0);
  const totalInterest = ledgers.reduce((sum, l) => sum + l.totalInterest, 0);
  const realizedPnlColor = totalRealizedPnl >= 0 ? 'number-positive' : 'number-negative';

  if (loading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-[var(--text-muted)] text-lg">載入中...</div>
      </div>
    );
  }

  return (
    <div className="min-h-screen py-8">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="flex justify-between items-center mb-8">
          <h1 className="text-2xl font-bold text-[var(--text-primary)]">外幣帳本</h1>
          <button
            onClick={() => setShowCreateForm(true)}
            className="btn-accent"
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

        {/* Summary Card */}
        <div className="card-dark p-6 mb-8">
          <h2 className="text-xl font-bold text-[var(--text-primary)] mb-6">總覽</h2>
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            <div className="metric-card">
              <p className="text-sm text-[var(--text-muted)] mb-1">淨投入</p>
              <p className="text-2xl font-bold text-[var(--text-primary)] number-display">{formatTWD(totalExchanged)}</p>
              <p className="text-sm text-[var(--text-muted)]">TWD</p>
            </div>
            <div className="metric-card">
              <p className="text-sm text-[var(--text-muted)] mb-1">目前成本</p>
              <p className="text-2xl font-bold text-[var(--text-primary)] number-display">{formatTWD(totalCost)}</p>
              <p className="text-sm text-[var(--text-muted)]">TWD</p>
            </div>
            <div className="metric-card">
              <p className="text-sm text-[var(--text-muted)] mb-1">已實現損益</p>
              <p className={`text-2xl font-bold number-display ${realizedPnlColor}`}>
                {totalRealizedPnl >= 0 ? '+' : ''}{formatTWD(totalRealizedPnl)}
              </p>
              <p className="text-sm text-[var(--text-muted)]">TWD</p>
            </div>
            <div className="metric-card">
              <p className="text-sm text-[var(--text-muted)] mb-1">利息收入</p>
              <p className="text-2xl font-bold text-[var(--text-primary)] number-display">{formatNumber(totalInterest)}</p>
              <p className="text-sm text-[var(--text-muted)]">外幣</p>
            </div>
          </div>
        </div>

        {/* Create Form Modal */}
        {showCreateForm && (
          <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50">
            <div className="card-dark p-6 w-full max-w-md m-4">
              <h2 className="text-xl font-bold text-[var(--text-primary)] mb-6">新增外幣帳本</h2>
              <form onSubmit={handleCreate} className="space-y-5">
                <div>
                  <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                    幣別代碼
                  </label>
                  <input
                    type="text"
                    value={currencyCode}
                    onChange={(e) => setCurrencyCode(e.target.value)}
                    className="input-dark w-full"
                    placeholder="USD"
                    maxLength={3}
                    required
                  />
                </div>
                <div>
                  <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                    帳本名稱
                  </label>
                  <input
                    type="text"
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    className="input-dark w-full"
                    placeholder="美金存款"
                    required
                  />
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
                    disabled={isSubmitting}
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
