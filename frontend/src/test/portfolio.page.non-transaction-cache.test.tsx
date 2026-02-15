import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { PortfolioPage } from '../pages/Portfolio';
import { usePortfolio } from '../contexts/PortfolioContext';
import { portfolioApi, transactionApi } from '../services/api';
import { invalidatePerformanceLocalStorageCache } from '../utils/cacheInvalidation';
import { Currency, StockMarket, TransactionType } from '../types';
import type { CreateStockTransactionRequest, Portfolio, PortfolioSummary, StockTransaction } from '../types';

vi.mock('../contexts/PortfolioContext', () => ({
  usePortfolio: vi.fn(),
}));

vi.mock('../services/api', () => ({
  portfolioApi: {
    getAll: vi.fn(),
    create: vi.fn(),
    getById: vi.fn(),
    getSummary: vi.fn(),
    calculateXirr: vi.fn(),
  },
  transactionApi: {
    getByPortfolio: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    delete: vi.fn(),
  },
  stockPriceApi: {
    getQuoteWithRate: vi.fn(),
  },
  marketDataApi: {
    getEuronextQuoteByTicker: vi.fn(),
  },
}));

vi.mock('../components/transactions/TransactionForm', () => ({
  TransactionForm: ({ onSubmit }: { onSubmit: (data: CreateStockTransactionRequest) => Promise<void> }) => (
    <button
      type="button"
      data-testid="mock-transaction-form-submit"
      onClick={() => {
        void onSubmit({
          portfolioId: 'portfolio-a',
          transactionDate: '2026-01-02',
          ticker: 'AAPL',
          transactionType: 2,
          shares: 1,
          pricePerShare: 100,
          fees: 1,
          market: 2,
          currency: 2,
        });
      }}
    >
      mock-submit-transaction
    </button>
  ),
}));

vi.mock('../components/transactions/TransactionList', () => ({
  TransactionList: ({
    onEdit,
    onDelete,
  }: {
    onEdit?: (transaction: StockTransaction) => void;
    onDelete?: (id: string) => Promise<void> | void;
  }) => {
    const editableTransaction: StockTransaction = {
      id: 'tx-update',
      portfolioId: 'portfolio-a',
      transactionDate: '2026-01-01T00:00:00.000Z',
      ticker: 'AAPL',
      transactionType: 2,
      shares: 1,
      pricePerShare: 100,
      exchangeRate: 30,
      fees: 1,
      notes: '',
      totalCostSource: 101,
      totalCostHome: 3030,
      hasExchangeRate: true,
      realizedPnlHome: 0,
      createdAt: '2026-01-01T00:00:00.000Z',
      updatedAt: '2026-01-01T00:00:00.000Z',
      splitRatio: 1,
      hasSplitAdjustment: false,
      market: 2,
      currency: 2,
    };

    return (
      <>
        <button
          type="button"
          data-testid="mock-transaction-list-edit"
          onClick={() => {
            onEdit?.(editableTransaction);
          }}
        >
          mock-edit-transaction
        </button>
        <button
          type="button"
          data-testid="mock-transaction-list-delete"
          onClick={() => {
            void onDelete?.('tx-delete');
          }}
        >
          mock-delete-transaction
        </button>
      </>
    );
  },
}));

vi.mock('../components/portfolio/PositionCard', () => ({
  PositionCard: () => null,
}));

vi.mock('../components/portfolio/PerformanceMetrics', () => ({
  PerformanceMetrics: () => null,
}));

vi.mock('../components/portfolio/PortfolioSelector', () => ({
  PortfolioSelector: () => null,
}));

vi.mock('../components/portfolio/CreatePortfolioForm', () => ({
  CreatePortfolioForm: () => null,
}));

vi.mock('../components/import', () => ({
  StockImportButton: () => null,
}));

vi.mock('../components/common', () => ({
  FileDropdown: () => null,
}));

vi.mock('../components/modals/ConfirmationModal', () => ({
  ConfirmationModal: ({
    isOpen,
    onConfirm,
  }: {
    isOpen: boolean;
    onConfirm: () => Promise<void> | void;
  }) => (
    isOpen
      ? (
          <button
            type="button"
            data-testid="mock-confirm-delete"
            onClick={() => {
              void onConfirm();
            }}
          >
            mock-confirm-delete
          </button>
        )
      : null
  ),
}));

vi.mock('../services/csvExport', () => ({
  exportTransactionsToCsv: vi.fn(),
}));

const mockedUsePortfolio = vi.mocked(usePortfolio);
const mockedPortfolioApi = vi.mocked(portfolioApi, { deep: true });
const mockedTransactionApi = vi.mocked(transactionApi, { deep: true });

const nowIso = '2026-01-01T00:00:00.000Z';

const portfolioA: Portfolio = {
  id: 'portfolio-a',
  baseCurrency: 'USD',
  homeCurrency: 'TWD',
  isActive: true,
  boundCurrencyLedgerId: 'ledger-a',
  createdAt: nowIso,
  updatedAt: nowIso,
};

const portfolioB: Portfolio = {
  id: 'portfolio-b',
  baseCurrency: 'USD',
  homeCurrency: 'TWD',
  isActive: true,
  boundCurrencyLedgerId: 'ledger-b',
  createdAt: nowIso,
  updatedAt: nowIso,
};

const allPortfolios = [portfolioA, portfolioB];

const buildEmptySummary = (portfolio: Portfolio): PortfolioSummary => ({
  portfolio,
  positions: [],
  totalCostHome: 0,
  totalValueHome: 0,
  totalUnrealizedPnlHome: 0,
  totalUnrealizedPnlPercentage: 0,
});

const buildStockTransaction = (id: string): StockTransaction => ({
  id,
  portfolioId: portfolioA.id,
  transactionDate: nowIso,
  ticker: 'AAPL',
  transactionType: TransactionType.Buy,
  shares: 1,
  pricePerShare: 100,
  exchangeRate: 30,
  fees: 1,
  notes: '',
  totalCostSource: 101,
  totalCostHome: 3030,
  hasExchangeRate: true,
  realizedPnlHome: 0,
  createdAt: nowIso,
  updatedAt: nowIso,
  splitRatio: 1,
  hasSplitAdjustment: false,
  market: StockMarket.US,
  currency: Currency.USD,
});

const clearPerformanceState = vi.fn();
const invalidateSharedCaches = vi.fn((options?: {
  performance?: boolean;
  assets?: boolean;
  clearPerformanceStorage?: boolean;
}) => {
  const shouldInvalidatePerformance = options?.performance !== false;
  const shouldClearPerformanceStorage = options?.clearPerformanceStorage !== false;

  if (shouldInvalidatePerformance && shouldClearPerformanceStorage) {
    invalidatePerformanceLocalStorageCache();
  }
});
const selectPortfolio = vi.fn();
const refreshPortfolios = vi.fn(async () => undefined);

function mockPortfolioContext(currentPortfolioId: string): void {
  mockedUsePortfolio.mockReturnValue({
    currentPortfolio: allPortfolios.find((portfolio) => portfolio.id === currentPortfolioId) ?? null,
    currentPortfolioId,
    isAllPortfolios: false,
    portfolios: allPortfolios,
    isLoading: false,
    selectPortfolio,
    refreshPortfolios,
    clearPerformanceState,
    invalidateSharedCaches,
    performanceVersion: 0,
  });
}

function setupDefaultApiMocks(): void {
  vi.clearAllMocks();
  localStorage.clear();

  mockedPortfolioApi.getAll.mockResolvedValue(allPortfolios);
  mockedPortfolioApi.create.mockResolvedValue(portfolioA);
  mockedPortfolioApi.getById.mockImplementation(async (portfolioId: string) => {
    if (portfolioId === portfolioB.id) {
      return portfolioB;
    }
    return portfolioA;
  });
  mockedPortfolioApi.getSummary.mockImplementation(async (portfolioId: string) => {
    if (portfolioId === portfolioB.id) {
      return buildEmptySummary(portfolioB);
    }
    return buildEmptySummary(portfolioA);
  });

  mockedTransactionApi.getByPortfolio.mockResolvedValue([] as StockTransaction[]);
  mockedTransactionApi.create.mockResolvedValue(buildStockTransaction('tx-created'));
  mockedTransactionApi.update.mockResolvedValue(buildStockTransaction('tx-updated'));
  mockedTransactionApi.delete.mockResolvedValue(undefined);
}

function seedHistoricalPerformanceCache(): void {
  localStorage.setItem('perf_years_portfolio-a', JSON.stringify({ years: [2025] }));
  localStorage.setItem('perf_data_portfolio-a_2025', JSON.stringify({ year: 2025 }));
  localStorage.setItem('quote_cache_AAPL', JSON.stringify({ quote: { price: 100 } }));
}

describe('PortfolioPage non-transaction performance cache behavior', () => {
  beforeEach(() => {
    setupDefaultApiMocks();
  });

  it('does not call clearPerformanceState on init or non-transaction portfolio switch', async () => {
    mockPortfolioContext(portfolioA.id);

    const { rerender } = render(
      <MemoryRouter>
        <PortfolioPage />
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(mockedPortfolioApi.getById).toHaveBeenCalledWith(portfolioA.id);
    });

    expect(clearPerformanceState).not.toHaveBeenCalled();
    expect(invalidateSharedCaches).not.toHaveBeenCalled();

    mockPortfolioContext(portfolioB.id);
    rerender(
      <MemoryRouter>
        <PortfolioPage />
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(mockedPortfolioApi.getById).toHaveBeenCalledWith(portfolioB.id);
    });

    expect(clearPerformanceState).not.toHaveBeenCalled();
    expect(invalidateSharedCaches).not.toHaveBeenCalled();
  });
});

describe('PortfolioPage transaction cache invalidation', () => {
  beforeEach(() => {
    setupDefaultApiMocks();
    mockPortfolioContext(portfolioA.id);
  });

  it('clears perf_years_/perf_data_ localStorage after create transaction mutation', async () => {
    seedHistoricalPerformanceCache();

    render(
      <MemoryRouter>
        <PortfolioPage />
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(mockedPortfolioApi.getById).toHaveBeenCalledWith(portfolioA.id);
    });

    const user = userEvent.setup();
    await user.click(screen.getByTestId('portfolio-add-transaction'));
    expect(localStorage.getItem('perf_years_portfolio-a')).not.toBeNull();
    expect(localStorage.getItem('perf_data_portfolio-a_2025')).not.toBeNull();
    await user.click(screen.getByTestId('mock-transaction-form-submit'));

    await waitFor(() => {
      expect(mockedTransactionApi.create).toHaveBeenCalledTimes(1);
      expect(invalidateSharedCaches).toHaveBeenCalledTimes(1);
      expect(localStorage.getItem('perf_years_portfolio-a')).toBeNull();
      expect(localStorage.getItem('perf_data_portfolio-a_2025')).toBeNull();
    });

    expect(localStorage.getItem('quote_cache_AAPL')).not.toBeNull();
  });

  it('clears perf_years_/perf_data_ localStorage after update transaction mutation', async () => {
    seedHistoricalPerformanceCache();
    mockedTransactionApi.getByPortfolio.mockResolvedValue([buildStockTransaction('tx-update')]);

    render(
      <MemoryRouter>
        <PortfolioPage />
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(screen.getByTestId('mock-transaction-list-edit')).toBeInTheDocument();
    });

    const user = userEvent.setup();
    await user.click(screen.getByTestId('mock-transaction-list-edit'));
    expect(localStorage.getItem('perf_years_portfolio-a')).not.toBeNull();
    expect(localStorage.getItem('perf_data_portfolio-a_2025')).not.toBeNull();
    await user.click(screen.getByTestId('mock-transaction-form-submit'));

    await waitFor(() => {
      expect(mockedTransactionApi.update).toHaveBeenCalledWith(
        'tx-update',
        expect.objectContaining({
          ticker: 'AAPL',
          transactionType: 2,
        })
      );
      expect(invalidateSharedCaches).toHaveBeenCalledTimes(1);
      expect(localStorage.getItem('perf_years_portfolio-a')).toBeNull();
      expect(localStorage.getItem('perf_data_portfolio-a_2025')).toBeNull();
    });

    expect(localStorage.getItem('quote_cache_AAPL')).not.toBeNull();
  });

  it('clears perf_years_/perf_data_ localStorage after delete transaction mutation', async () => {
    seedHistoricalPerformanceCache();
    mockedTransactionApi.getByPortfolio.mockResolvedValue([buildStockTransaction('tx-delete')]);

    render(
      <MemoryRouter>
        <PortfolioPage />
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(screen.getByTestId('mock-transaction-list-delete')).toBeInTheDocument();
    });

    const user = userEvent.setup();
    expect(localStorage.getItem('perf_years_portfolio-a')).not.toBeNull();
    expect(localStorage.getItem('perf_data_portfolio-a_2025')).not.toBeNull();
    await user.click(screen.getByTestId('mock-transaction-list-delete'));
    await user.click(screen.getByTestId('mock-confirm-delete'));

    await waitFor(() => {
      expect(mockedTransactionApi.delete).toHaveBeenCalledWith('tx-delete');
      expect(invalidateSharedCaches).toHaveBeenCalledTimes(1);
      expect(localStorage.getItem('perf_years_portfolio-a')).toBeNull();
      expect(localStorage.getItem('perf_data_portfolio-a_2025')).toBeNull();
    });

    expect(localStorage.getItem('quote_cache_AAPL')).not.toBeNull();
  });
});
