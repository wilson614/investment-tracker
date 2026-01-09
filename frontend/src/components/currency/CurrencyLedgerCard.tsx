import { useEffect, useState, useRef } from 'react';
import { stockPriceApi } from '../../services/api';
import type { CurrencyLedgerSummary } from '../../types';

interface CurrencyLedgerCardProps {
  ledger: CurrencyLedgerSummary;
  onClick?: () => void;
}

// Cache key for exchange rate
const getRateCacheKey = (from: string, to: string) => `rate_cache_${from}_${to}`;

interface CachedRate {
  rate: number;
  cachedAt: string;
}

// Load cached rate from localStorage
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
  // Initialize from cache immediately to prevent flickering
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

  const formatTime = (date: Date) => {
    return date.toLocaleTimeString('zh-TW', { hour: '2-digit', minute: '2-digit', hour12: false });
  };

  // Fetch exchange rate on mount
  useEffect(() => {
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
  }, [ledger.ledger.currencyCode, ledger.ledger.homeCurrency]);

  // Calculate TWD equivalent
  const twdEquivalent = currentRate && ledger.balance > 0 
    ? ledger.balance * currentRate 
    : null;

  return (
    <div
      className="card-dark p-5 cursor-pointer hover:border-[var(--border-hover)] transition-all"
      onClick={onClick}
    >
      {/* Header: Currency @ Rate */}
      <div className="flex justify-between items-start mb-4">
        <div className="flex items-center gap-2 flex-wrap">
          <h3 className="text-lg font-bold text-[var(--accent-cream)]">
            {ledger.ledger.currencyCode}
          </h3>
          {currentRate && (
            <span className="text-sm text-[var(--text-muted)]">
              @ {formatNumber(currentRate, 2)}
            </span>
          )}
        </div>
        {rateUpdatedAt && (
          <span className="text-xs text-[var(--text-muted)]">
            {formatTime(rateUpdatedAt)}
          </span>
        )}
      </div>

      {/* Balance (large) */}
      <div className="mb-4">
        <p className="text-2xl font-bold text-[var(--accent-peach)] number-display">
          {formatNumber(ledger.balance, 2)}
        </p>
        {twdEquivalent !== null && (
          <p className="text-sm text-[var(--text-muted)] mt-1">
            ≈ {formatTWD(twdEquivalent)} TWD
          </p>
        )}
      </div>

      {/* Metrics */}
      <div className="space-y-2 text-base">
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

        {(ledger.totalInterest ?? 0) > 0 && (
          <div className="flex justify-between">
            <span className="text-[var(--text-muted)]">利息收入:</span>
            <span className="font-medium text-[var(--accent-peach)] number-display">
              {formatNumber(ledger.totalInterest, 2)} {ledger.ledger.currencyCode}
            </span>
          </div>
        )}
      </div>
    </div>
  );
}
