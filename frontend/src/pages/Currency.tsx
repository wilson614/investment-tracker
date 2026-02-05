/**
 * Currency Page
 *
 * 外幣帳本首頁：列出使用者的外幣帳本摘要。
 *
 * 注意：帳本會隨投資組合自動建立，用戶不需要手動建立帳本。
 */
import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { currencyLedgerApi } from '../services/api';
import { CurrencyLedgerCard } from '../components/currency/CurrencyLedgerCard';
import { Skeleton } from '../components/common';
import type { CurrencyLedgerSummary } from '../types';

const LEDGERS_CACHE_KEY = 'currency_ledgers_cache';

interface CachedLedgers {
  data: CurrencyLedgerSummary[];
  cachedAt: string;
}

const loadCachedLedgers = (): CurrencyLedgerSummary[] => {
  try {
    const cached = localStorage.getItem(LEDGERS_CACHE_KEY);
    if (cached) {
      const { data }: CachedLedgers = JSON.parse(cached);
      return data;
    }
  } catch {
    // Ignore cache errors
  }
  return [];
};

export default function Currency() {
  const navigate = useNavigate();
  const cachedData = loadCachedLedgers();
  const [ledgers, setLedgers] = useState<CurrencyLedgerSummary[]>(cachedData);
  const [loading, setLoading] = useState(cachedData.length === 0);
  const [error, setError] = useState<string | null>(null);

  /**
   * 載入所有外幣帳本摘要。
   */
  const loadLedgers = async () => {
    try {
      // No manual scroll restoration needed - let the browser handle it
      // by not wiping the content during refresh.

      const data = await currencyLedgerApi.getAll();
      setLedgers(data);

      // Save to cache
      try {
        localStorage.setItem(
          LEDGERS_CACHE_KEY,
          JSON.stringify({
            data,
            cachedAt: new Date().toISOString(),
          })
        );
      } catch {
        // Ignore cache errors
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load currency ledgers');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadLedgers();
  }, []);

  if (loading) {
    return (
      <div className="min-h-screen py-8">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="mb-8">
            <h1 className="text-2xl font-bold text-[var(--text-primary)]">帳本</h1>
            <div className="mt-2">
              <Skeleton width="w-80" height="h-5" />
            </div>
          </div>
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {[1, 2, 3].map((i) => (
              <div key={i} className="card-dark p-5" style={{ minHeight: 212 }}>
                <div className="flex justify-between items-start mb-4">
                  <div className="flex items-center gap-2">
                    <Skeleton width="w-12" height="h-6" />
                    <Skeleton width="w-16" height="h-5" />
                  </div>
                </div>
                <div className="mb-4">
                  <Skeleton width="w-32" height="h-8" className="mb-2" />
                  <Skeleton width="w-24" height="h-5" />
                </div>
                <div className="space-y-3">
                  <div className="flex justify-between">
                    <Skeleton width="w-16" height="h-5" />
                    <Skeleton width="w-20" height="h-5" />
                  </div>
                  <div className="flex justify-between">
                    <Skeleton width="w-16" height="h-5" />
                    <Skeleton width="w-20" height="h-5" />
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen py-8">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="mb-8">
          <h1 className="text-2xl font-bold text-[var(--text-primary)]">帳本</h1>
          <p className="text-base text-[var(--text-secondary)] mt-2">
            帳本會隨投資組合自動建立，無需手動新增。
          </p>
          <button
            type="button"
            onClick={() => navigate('/portfolio')}
            className="btn-dark mt-4"
          >
            前往投資組合
          </button>
        </div>

        {error && (
          <div className="bg-[var(--color-danger-soft)] border border-[var(--color-danger)] text-[var(--color-danger)] p-4 rounded-lg mb-6 flex justify-between items-center">
            <span className="text-base">{error}</span>
            <button onClick={() => setError(null)} className="hover:underline text-base">
              關閉
            </button>
          </div>
        )}

        {/* Ledger List */}
        {ledgers.length === 0 ? (
          <div className="card-dark p-12 text-center">
            <p className="text-[var(--text-muted)] text-lg">尚無帳本</p>
            <p className="text-base text-[var(--text-muted)] mt-2">
              帳本會在建立投資組合時自動建立。請先前往「投資組合」建立投資組合。
            </p>
            <button
              type="button"
              onClick={() => navigate('/portfolio')}
              className="btn-accent mt-6"
            >
              前往投資組合
            </button>
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
