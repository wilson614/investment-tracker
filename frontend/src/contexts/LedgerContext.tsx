import { createContext, useContext, useState, useCallback, useEffect, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import type { CurrencyLedgerSummary } from '../types';
import { currencyLedgerApi } from '../services/api';
import { useAuth } from '../hooks/useAuth';

interface LedgerContextValue {
  // Current selected ledger
  currentLedger: CurrencyLedgerSummary | null;
  currentLedgerId: string | null;

  // All ledgers
  ledgers: CurrencyLedgerSummary[];
  isLoading: boolean;

  // Actions
  selectLedger: (ledgerId: string) => void;
  refreshLedgers: () => Promise<void>;
}

const LedgerContext = createContext<LedgerContextValue | null>(null);

// localStorage key for persisting selected ledger
const SELECTED_LEDGER_KEY = 'selected_ledger_id';

// query key for ledger list
const LEDGERS_QUERY_KEY = ['currencyLedgers'] as const;

export function LedgerProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const [currentLedgerId, setCurrentLedgerId] = useState<string | null>(() => {
    // Load from localStorage on init
    try {
      return localStorage.getItem(SELECTED_LEDGER_KEY);
    } catch {
      return null;
    }
  });

  const {
    data: ledgers = [],
    isLoading,
    refetch,
  } = useQuery({
    queryKey: LEDGERS_QUERY_KEY,
    queryFn: () => currencyLedgerApi.getAll(),
    enabled: !!user,
  });

  const currentLedger = ledgers.find((ledger) => ledger.ledger.id === currentLedgerId) ?? null;

  // Keep selection valid when ledger list changes
  useEffect(() => {
    if (!user) {
      setCurrentLedgerId(null);
      return;
    }

    if (ledgers.length > 0) {
      const selectedExists = currentLedgerId && ledgers.some((ledger) => ledger.ledger.id === currentLedgerId);
      if (!selectedExists) {
        const newId = ledgers[0].ledger.id;
        setCurrentLedgerId(newId);
        try {
          localStorage.setItem(SELECTED_LEDGER_KEY, newId);
        } catch {
          // Ignore localStorage errors
        }
      }
    } else {
      setCurrentLedgerId(null);
      try {
        localStorage.removeItem(SELECTED_LEDGER_KEY);
      } catch {
        // Ignore localStorage errors
      }
    }
  }, [user, ledgers, currentLedgerId]);

  const selectLedger = useCallback((ledgerId: string) => {
    if (ledgerId === currentLedgerId) return;

    setCurrentLedgerId(ledgerId);
    try {
      localStorage.setItem(SELECTED_LEDGER_KEY, ledgerId);
    } catch {
      // Ignore localStorage errors
    }
  }, [currentLedgerId]);

  const refreshLedgers = useCallback(async () => {
    await refetch();
  }, [refetch]);

  return (
    <LedgerContext.Provider
      value={{
        currentLedger,
        currentLedgerId,
        ledgers,
        isLoading: user ? isLoading : false,
        selectLedger,
        refreshLedgers,
      }}
    >
      {children}
    </LedgerContext.Provider>
  );
}

export function useLedger() {
  const context = useContext(LedgerContext);
  if (!context) {
    throw new Error('useLedger must be used within a LedgerProvider');
  }
  return context;
}
