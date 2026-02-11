import { createContext, useContext, useState, useCallback, useEffect, useRef, type ReactNode } from 'react';
import type { Portfolio } from '../types';
import { portfolioApi } from '../services/api';
import { useAuth } from '../hooks/useAuth';

interface PortfolioContextValue {
  // Current selected portfolio
  currentPortfolio: Portfolio | null;
  currentPortfolioId: string | null;
  isAllPortfolios: boolean;

  // All portfolios
  portfolios: Portfolio[];
  isLoading: boolean;

  // Actions
  selectPortfolio: (portfolioId: string) => void;
  refreshPortfolios: () => Promise<void>;

  // Performance state management
  clearPerformanceState: () => void;
  performanceVersion: number; // Increment to trigger re-fetch
}

const PortfolioContext = createContext<PortfolioContextValue | null>(null);

// localStorage key for persisting selected portfolio
const SELECTED_PORTFOLIO_KEY = 'selected_portfolio_id';

export function PortfolioProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const [portfolios, setPortfolios] = useState<Portfolio[]>([]);
  const [currentPortfolioId, setCurrentPortfolioId] = useState<string | null>(() => {
    // Load from localStorage on init
    try {
      return localStorage.getItem(SELECTED_PORTFOLIO_KEY) ?? 'all';
    } catch {
      return 'all';
    }
  });
  const [isLoading, setIsLoading] = useState(true);
  const [performanceVersion, setPerformanceVersion] = useState(0);

  // Ref to track current ID for refresh logic without triggering re-creation
  const currentPortfolioIdRef = useRef(currentPortfolioId);
  useEffect(() => {
    currentPortfolioIdRef.current = currentPortfolioId;
  }, [currentPortfolioId]);

  const isAllPortfolios = currentPortfolioId === 'all';
  const currentPortfolio = isAllPortfolios
    ? null
    : portfolios.find(p => p.id === currentPortfolioId) ?? null;

  const refreshPortfolios = useCallback(async () => {
    try {
      setIsLoading(true);
      const data = await portfolioApi.getAll();
      setPortfolios(data);

      // Keep existing selection if valid or "all", otherwise fallback to "all"
      const currentId = currentPortfolioIdRef.current;
      if (data.length > 0) {
        const isAllSelection = currentId === 'all';
        const selectedExists = !!currentId && data.some(p => p.id === currentId);

        if (!isAllSelection && !selectedExists) {
          setCurrentPortfolioId('all');
          localStorage.setItem(SELECTED_PORTFOLIO_KEY, 'all');
        }
      } else {
        setCurrentPortfolioId('all');
        localStorage.setItem(SELECTED_PORTFOLIO_KEY, 'all');
      }
    } catch (error) {
      console.error('Failed to load portfolios:', error);
    } finally {
      setIsLoading(false);
    }
  }, []);

  const selectPortfolio = useCallback((portfolioId: string) => {
    if (portfolioId === currentPortfolioId) return;

    // Clear performance state immediately (FR-100: within 100ms)
    setPerformanceVersion(v => v + 1);

    // Update selection
    setCurrentPortfolioId(portfolioId);
    try {
      localStorage.setItem(SELECTED_PORTFOLIO_KEY, portfolioId);
    } catch {
      // Ignore localStorage errors
    }
  }, [currentPortfolioId]);

  const clearPerformanceState = useCallback(() => {
    setPerformanceVersion(v => v + 1);
  }, []);

  // Load portfolios on mount - only when user is logged in
  useEffect(() => {
    if (user) {
      refreshPortfolios();
    } else {
      // Clear state when user logs out
      setPortfolios([]);
      setCurrentPortfolioId('all');
      setIsLoading(false);
    }
  }, [user, refreshPortfolios]);

  return (
    <PortfolioContext.Provider
      value={{
        currentPortfolio,
        currentPortfolioId,
        isAllPortfolios,
        portfolios,
        isLoading,
        selectPortfolio,
        refreshPortfolios,
        clearPerformanceState,
        performanceVersion,
      }}
    >
      {children}
    </PortfolioContext.Provider>
  );
}

export function usePortfolio() {
  const context = useContext(PortfolioContext);
  if (!context) {
    throw new Error('usePortfolio must be used within a PortfolioProvider');
  }
  return context;
}
