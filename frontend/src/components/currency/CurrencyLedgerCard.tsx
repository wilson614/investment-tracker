/**
 * CurrencyLedgerCard
 *
 * 外幣帳本卡片：顯示外幣餘額、淨投入、換匯均價，並在卡片上顯示即時匯率與更新時間。
 *
 * 特色：
 * - 先讀取 localStorage 的匯率快取，避免初次 render 閃爍。
 * - 掛載後只抓一次匯率（用 `hasFetchedRate` 防止重複）。
 */
import { useEffect, useState, useRef } from 'react';
import { stockPriceApi } from '../../services/api';
import type { CurrencyLedgerSummary } from '../../types';
import { Skeleton } from '../common';

interface CurrencyLedgerCardProps {
  /** 要顯示的帳本摘要 */
  ledger: CurrencyLedgerSummary;
  /** 點擊卡片 callback */
  onClick?: () => void;
}

/**
 * 匯率 localStorage 快取 key。
 */
const getRateCacheKey = (from: string, to: string) => `rate_cache_${from}_${to}`;

interface CachedRate {
  rate: number;
  cachedAt: string;
}

/**
 * 從 localStorage 載入匯率快取。
 */
const loadCachedRate = (from: string, to: string): CachedRate | null => {
  try {
    const cached = localStorage.getItem(getRateCacheKey(from, to));
    if (cached) {
      return JSON.parse(cached);
    }
  } catch {
    // Ignore cache errors
  }
  return null;
};

export function CurrencyLedgerCard({ ledger, onClick }: CurrencyLedgerCardProps) {
  const isHomeCurrencyLedger = ledger.ledger.currencyCode === ledger.ledger.homeCurrency;
  const isTwdLedger = ledger.ledger.currencyCode === 'TWD';

  // 先用快取初始化，避免初次 render 因等待 API 而閃爍。
  const cachedData = loadCachedRate(ledger.ledger.currencyCode, ledger.ledger.homeCurrency);
  const [currentRate, setCurrentRate] = useState<number | null>(cachedData?.rate ?? null);
  const [rateUpdatedAt, setRateUpdatedAt] = useState<Date | null>(
    cachedData?.cachedAt ? new Date(cachedData.cachedAt) : null
  );
  const hasFetchedRate = useRef(false);

  const formatNumber = (value: number | null | undefined, decimals = 2) => {
    if (value == null || isNaN(value)) return '-';
    return value.toLocaleString('zh-TW', {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    });
  };

  // Format TWD as integer
  const formatTWD = (value: number | null | undefined) => {
    if (value == null || isNaN(value)) return '-';
    return Math.round(value).toLocaleString('zh-TW');
  };

  const formatLedgerCurrency = (value: number | null | undefined, decimals = 2) => {
    return isTwdLedger ? formatTWD(value) : formatNumber(value, decimals);
  };

  const formatTime = (date: Date) => {
    return date.toLocaleTimeString('zh-TW', { hour: '2-digit', minute: '2-digit', hour12: false });
  };

  // 掛載後抓取即時匯率（失敗時會保留快取值）。
  useEffect(() => {
    if (isHomeCurrencyLedger) return;

    if (!hasFetchedRate.current) {
      hasFetchedRate.current = true;
      stockPriceApi.getExchangeRate(ledger.ledger.currencyCode, ledger.ledger.homeCurrency)
        .then(rateResponse => {
          if (rateResponse?.rate) {
            const now = new Date();
            setCurrentRate(rateResponse.rate);
            setRateUpdatedAt(now);
            // Save to cache
            try {
              localStorage.setItem(
                getRateCacheKey(ledger.ledger.currencyCode, ledger.ledger.homeCurrency),
                JSON.stringify({ rate: rateResponse.rate, cachedAt: now.toISOString() })
              );
            } catch {
              // Ignore cache errors
            }
          }
        })
        .catch(() => {
          // Silently fail, keep cached value
        });
    }
  }, [ledger.ledger.currencyCode, ledger.ledger.homeCurrency, isHomeCurrencyLedger]);

  // 依目前匯率計算約當台幣（只在 balance > 0 時顯示）。
  const twdEquivalent = !isHomeCurrencyLedger && currentRate && ledger.balance > 0
    ? ledger.balance * currentRate
    : null;

  return (
    <div
      className="card-dark p-5 cursor-pointer hover:border-[var(--border-hover)] transition-all"
      style={{ minHeight: 212 }}
      onClick={onClick}
    >
      {/* Header: Currency @ Rate */}
      <div className="flex justify-between items-start mb-4">
        <div className="flex items-center gap-2">
          <h3 className="text-lg font-bold text-[var(--accent-cream)]">
            {ledger.ledger.currencyCode}
          </h3>
          {!isHomeCurrencyLedger && (
            currentRate ? (
              <span className="text-sm text-[var(--text-muted)]">
                @ {formatNumber(currentRate, 2)}
              </span>
            ) : (
              <Skeleton width="w-16" height="h-5" />
            )
          )}
        </div>
        {!isHomeCurrencyLedger && rateUpdatedAt && (
          <span className="text-xs text-[var(--text-muted)]">
            {formatTime(rateUpdatedAt)}
          </span>
        )}
      </div>

      {/* Balance (large) */}
      <div className="mb-4">
        <p
          className={`text-2xl font-bold number-display ${ledger.balance < 0 ? 'text-[var(--color-danger)]' : 'text-[var(--accent-peach)]'}`}
          title={ledger.balance < 0 ? '餘額為負' : undefined}
        >
          {formatLedgerCurrency(ledger.balance, 2)}
        </p>
        {twdEquivalent !== null ? (
          <p className="text-sm text-[var(--text-muted)] mt-1">
            ≈ {formatTWD(twdEquivalent)} TWD
          </p>
        ) : (!isHomeCurrencyLedger && ledger.balance > 0) ? (
          <div className="mt-1">
            <Skeleton width="w-24" height="h-5" />
          </div>
        ) : null}
      </div>

      {/* Metrics */}
      <div className="space-y-2 text-base">
        {!isHomeCurrencyLedger && (
          <>
            <div className="flex justify-between">
              <span className="text-[var(--text-muted)]">淨投入:</span>
              <span className="font-medium text-[var(--text-primary)] number-display">
                {formatTWD(ledger.totalExchanged)} {ledger.ledger.homeCurrency}
              </span>
            </div>

            <div className="flex justify-between">
              <span className="text-[var(--text-muted)]">換匯均價:</span>
              <span className="font-medium text-[var(--text-primary)] number-display">
                {formatNumber(ledger.averageExchangeRate, 4)}
              </span>
            </div>
          </>
        )}

        {(ledger.totalInterest ?? 0) > 0 && (
          <div className="flex justify-between">
            <span className="text-[var(--text-muted)]">利息收入:</span>
            <span className="font-medium text-[var(--accent-peach)] number-display">
              {formatLedgerCurrency(ledger.totalInterest, 2)} {ledger.ledger.currencyCode}
            </span>
          </div>
        )}
      </div>
    </div>
  );
}
