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

  const totalCost = ledgers.reduce((sum, l) => sum + l.totalCostHome, 0);
  const totalRealizedPnl = ledgers.reduce((sum, l) => sum + l.realizedPnl, 0);

  if (loading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  return (
    <div className="container mx-auto px-4 py-8">
      <div className="flex justify-between items-center mb-6">
        <h1 className="text-2xl font-bold text-gray-900">外幣帳本</h1>
        <button
          onClick={() => setShowCreateForm(true)}
          className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors"
        >
          新增帳本
        </button>
      </div>

      {error && (
        <div className="bg-red-50 text-red-700 p-4 rounded-lg mb-6">
          {error}
          <button
            onClick={() => setError(null)}
            className="ml-2 underline"
          >
            關閉
          </button>
        </div>
      )}

      {/* Summary Card */}
      <div className="bg-gradient-to-r from-blue-600 to-blue-700 rounded-lg shadow-lg p-6 text-white mb-8">
        <h2 className="text-lg font-medium mb-4">總覽</h2>
        <div className="grid grid-cols-2 gap-4">
          <div>
            <p className="text-blue-200 text-sm">總投入成本</p>
            <p className="text-2xl font-bold">{formatNumber(totalCost)} TWD</p>
          </div>
          <div>
            <p className="text-blue-200 text-sm">已實現損益</p>
            <p className={`text-2xl font-bold ${totalRealizedPnl >= 0 ? 'text-green-300' : 'text-red-300'}`}>
              {totalRealizedPnl >= 0 ? '+' : ''}{formatNumber(totalRealizedPnl)} TWD
            </p>
          </div>
        </div>
      </div>

      {/* Create Form Modal */}
      {showCreateForm && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 w-full max-w-md">
            <h2 className="text-xl font-bold mb-4">新增外幣帳本</h2>
            <form onSubmit={handleCreate} className="space-y-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  幣別代碼
                </label>
                <input
                  type="text"
                  value={currencyCode}
                  onChange={(e) => setCurrencyCode(e.target.value)}
                  className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                  placeholder="USD"
                  maxLength={3}
                  required
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  帳本名稱
                </label>
                <input
                  type="text"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                  placeholder="美金存款"
                  required
                />
              </div>
              <div className="flex gap-3">
                <button
                  type="button"
                  onClick={() => setShowCreateForm(false)}
                  className="flex-1 px-4 py-2 text-gray-700 bg-gray-100 rounded-lg hover:bg-gray-200"
                >
                  取消
                </button>
                <button
                  type="submit"
                  disabled={isSubmitting}
                  className="flex-1 px-4 py-2 text-white bg-blue-600 rounded-lg hover:bg-blue-700 disabled:opacity-50"
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
        <div className="text-center py-12 text-gray-500">
          <p>尚無外幣帳本</p>
          <p className="text-sm mt-2">點擊「新增帳本」開始追蹤您的外幣資產</p>
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
  );
}
