/**
 * Currency Page
 *
 * 外幣帳本入口頁：固定使用 /ledger URL，並直接呈現目前選取的帳本內容。
 */
import { useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { useLedger } from '../contexts/LedgerContext';
import CurrencyDetail from './CurrencyDetail';

export default function Currency() {
  const navigate = useNavigate();
  const { ledgers, currentLedgerId, isLoading } = useLedger();

  const targetLedgerId = useMemo(() => {
    if (currentLedgerId && ledgers.some((ledger) => ledger.ledger.id === currentLedgerId)) {
      return currentLedgerId;
    }

    return ledgers[0]?.ledger.id ?? null;
  }, [currentLedgerId, ledgers]);

  if (isLoading && !targetLedgerId) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-[var(--text-muted)] text-lg">載入中...</div>
      </div>
    );
  }

  if (!isLoading && ledgers.length === 0) {
    return (
      <div className="min-h-screen py-8">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="mb-8">
            <h1 className="text-2xl font-bold text-[var(--text-primary)]">帳本</h1>
          </div>

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
        </div>
      </div>
    );
  }

  if (!targetLedgerId) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-[var(--text-muted)] text-lg">載入中...</div>
      </div>
    );
  }

  return <CurrencyDetail ledgerId={targetLedgerId} />;
}
