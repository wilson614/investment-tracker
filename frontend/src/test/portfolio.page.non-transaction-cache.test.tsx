import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { PortfolioPage } from '../pages/Portfolio';
import { usePortfolio } from '../contexts/PortfolioContext';
import { portfolioApi, transactionApi, currencyLedgerApi, stockPriceApi } from '../services/api';
import { invalidatePerformanceLocalStorageCache } from '../utils/cacheInvalidation';
import { Currency, StockMarket, TransactionType } from '../types';
import type { CreateStockTransactionRequest, Portfolio, PortfolioSummary, StockTransaction, XirrResult } from '../types';

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
  currencyLedgerApi: {
    getAll: vi.fn(),
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
  PositionCard: ({
    position,
  }: {
    position: { ticker: string; market?: number };
  }) => (
    <article data-testid={`mock-position-card-${position.ticker}-${position.market ?? 'default'}`}>
      {position.ticker}
    </article>
  ),
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
  StockImportButton: ({
    onImportComplete,
  }: {
    onImportComplete?: () => void;
  }) => (
    <button
      type="button"
      data-testid="mock-stock-import-complete"
      onClick={() => {
        onImportComplete?.();
      }}
    >
      mock-stock-import-complete
    </button>
  ),
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
const mockedCurrencyLedgerApi = vi.mocked(currencyLedgerApi, { deep: true });
const mockedStockPriceApi = vi.mocked(stockPriceApi, { deep: true });

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

const buildSummaryWithSinglePosition = (
  portfolio: Portfolio,
  ticker = 'AAPL',
): PortfolioSummary => ({
  portfolio,
  positions: [
    {
      ticker,
      market: StockMarket.US,
      currency: 'USD',
      totalShares: 10,
      totalCostSource: 1000,
      averageCostPerShareSource: 100,
      totalCostHome: 30000,
      averageCostPerShareHome: 3000,
    },
  ],
  totalCostHome: 30000,
  totalValueHome: 0,
  totalUnrealizedPnlHome: 0,
  totalUnrealizedPnlPercentage: 0,
});

const buildSummaryWithDuplicateTickerAcrossMarkets = (portfolio: Portfolio): PortfolioSummary => ({
  portfolio,
  positions: [
    {
      ticker: 'ABC',
      market: StockMarket.US,
      currency: 'USD',
      totalShares: 10,
      totalCostSource: 1000,
      averageCostPerShareSource: 100,
      totalCostHome: 30000,
      averageCostPerShareHome: 3000,
    },
    {
      ticker: 'ABC',
      market: StockMarket.UK,
      currency: 'USD',
      totalShares: 5,
      totalCostSource: 500,
      averageCostPerShareSource: 100,
      totalCostHome: 15000,
      averageCostPerShareHome: 3000,
    },
  ],
  totalCostHome: 45000,
  totalValueHome: 0,
  totalUnrealizedPnlHome: 0,
  totalUnrealizedPnlPercentage: 0,
});

const emptyXirrResult: XirrResult = {
  xirr: null,
  xirrPercentage: null,
  cashFlowCount: 0,
  asOfDate: nowIso,
  earliestTransactionDate: null,
  missingExchangeRates: null,
};

const buildQuoteResponse = (symbol: string) => ({
  symbol,
  name: `${symbol} Name`,
  price: 150,
  market: StockMarket.US,
  source: 'Yahoo',
  fetchedAt: nowIso,
  exchangeRate: 30,
  exchangeRatePair: 'USD/TWD',
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

function mockPortfolioContext(
  currentPortfolioId: string | null,
  options?: {
    isAllPortfolios?: boolean;
    portfolios?: Portfolio[];
  },
): void {
  const portfolios = options?.portfolios ?? allPortfolios;
  const isAllPortfolios = options?.isAllPortfolios ?? false;

  mockedUsePortfolio.mockReturnValue({
    currentPortfolio:
      !currentPortfolioId || isAllPortfolios
        ? null
        : portfolios.find((portfolio) => portfolio.id === currentPortfolioId) ?? null,
    currentPortfolioId,
    isAllPortfolios,
    portfolios,
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
  mockedPortfolioApi.calculateXirr.mockResolvedValue(emptyXirrResult);

  mockedTransactionApi.getByPortfolio.mockResolvedValue([] as StockTransaction[]);
  mockedTransactionApi.create.mockResolvedValue(buildStockTransaction('tx-created'));
  mockedTransactionApi.update.mockResolvedValue(buildStockTransaction('tx-updated'));
  mockedTransactionApi.delete.mockResolvedValue(undefined);
  mockedStockPriceApi.getQuoteWithRate.mockImplementation(async (_market, ticker: string) =>
    buildQuoteResponse(ticker),
  );
  mockedCurrencyLedgerApi.getAll.mockResolvedValue([
    {
      ledger: {
        id: 'ledger-a',
        currencyCode: 'USD',
        name: 'USD Ledger',
        homeCurrency: 'TWD',
        isActive: true,
        createdAt: nowIso,
        updatedAt: nowIso,
      },
      balance: 0,
      averageExchangeRate: 1,
      totalExchanged: 0,
      totalSpentOnStocks: 0,
      totalInterest: 0,
      totalCost: 0,
      realizedPnl: 0,
      recentTransactions: [],
    },
    {
      ledger: {
        id: 'ledger-b',
        currencyCode: 'USD',
        name: 'USD Ledger B',
        homeCurrency: 'TWD',
        isActive: true,
        createdAt: nowIso,
        updatedAt: nowIso,
      },
      balance: 0,
      averageExchangeRate: 1,
      totalExchanged: 0,
      totalSpentOnStocks: 0,
      totalInterest: 0,
      totalCost: 0,
      realizedPnl: 0,
      recentTransactions: [],
    },
  ]);
}

function seedHistoricalPerformanceCache(): void {
  localStorage.setItem('perf_years_portfolio-a', JSON.stringify({ years: [2025] }));
  localStorage.setItem('perf_data_portfolio-a_2025', JSON.stringify({ year: 2025 }));
  localStorage.setItem('quote_cache_AAPL_2', JSON.stringify({ quote: { price: 100 } }));
}

function seedCompositeQuoteCacheForDuplicateTicker(): void {
  localStorage.setItem('quote_cache_ABC_2', JSON.stringify({
    quote: { price: 100, exchangeRate: 30 },
    updatedAt: nowIso,
    market: StockMarket.US,
  }));
  localStorage.setItem('quote_cache_ABC_3', JSON.stringify({
    quote: { price: 110, exchangeRate: 40 },
    updatedAt: nowIso,
    market: StockMarket.UK,
  }));
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

describe('PortfolioPage quote cache key consistency', () => {
  it('shows net-positive holdings disclosure with rendered position cards', async () => {
    setupDefaultApiMocks();
    mockPortfolioContext(portfolioA.id);

    mockedPortfolioApi.getSummary.mockResolvedValue(buildSummaryWithDuplicateTickerAcrossMarkets(portfolioA));

    render(
      <MemoryRouter>
        <PortfolioPage />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(screen.getByText('持倉清單僅顯示淨股數（買入－賣出）大於 0 的標的。')).toBeInTheDocument();
      expect(screen.getByTestId('mock-position-card-ABC-2')).toBeInTheDocument();
    });
  });

  it('re-fetches quote once after switching portfolio context', async () => {
    setupDefaultApiMocks();
    mockPortfolioContext(portfolioA.id);

    mockedPortfolioApi.getSummary.mockImplementation(async (portfolioId: string) => {
      if (portfolioId === portfolioA.id) {
        return buildSummaryWithSinglePosition(portfolioA, 'AAPL');
      }

      if (portfolioId === portfolioB.id) {
        return buildSummaryWithSinglePosition(portfolioB, 'MSFT');
      }

      return buildEmptySummary(portfolioA);
    });

    const { rerender } = render(
      <MemoryRouter>
        <PortfolioPage />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(mockedPortfolioApi.getById).toHaveBeenCalledWith(portfolioA.id);
      expect(mockedStockPriceApi.getQuoteWithRate).toHaveBeenCalledWith(
        StockMarket.US,
        'AAPL',
        portfolioA.homeCurrency,
      );
    });

    const quoteCallsBeforeSwitch = mockedStockPriceApi.getQuoteWithRate.mock.calls.length;

    mockPortfolioContext(portfolioB.id);
    rerender(
      <MemoryRouter>
        <PortfolioPage />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(mockedPortfolioApi.getById).toHaveBeenCalledWith(portfolioB.id);
      expect(mockedStockPriceApi.getQuoteWithRate).toHaveBeenCalledWith(
        StockMarket.US,
        'MSFT',
        portfolioB.homeCurrency,
      );
    });

    expect(mockedStockPriceApi.getQuoteWithRate.mock.calls.length).toBeGreaterThan(quoteCallsBeforeSwitch);
  });

  it('import completion reloads currently selected portfolio in multi-portfolio scenario', async () => {
    setupDefaultApiMocks();
    mockPortfolioContext(portfolioB.id);

    render(
      <MemoryRouter>
        <PortfolioPage />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(mockedPortfolioApi.getById).toHaveBeenCalledWith(portfolioB.id);
    });

    const getByIdCallsBeforeImport = mockedPortfolioApi.getById.mock.calls.length;
    const getAllCallsBeforeImport = mockedPortfolioApi.getAll.mock.calls.length;

    const user = userEvent.setup();
    await user.click(screen.getByTestId('mock-stock-import-complete'));

    await waitFor(() => {
      expect(mockedPortfolioApi.getById.mock.calls.length).toBeGreaterThan(getByIdCallsBeforeImport);
    });

    expect(mockedPortfolioApi.getById).toHaveBeenLastCalledWith(portfolioB.id);
    expect(mockedPortfolioApi.getAll.mock.calls.length).toBe(getAllCallsBeforeImport);
  });

  it('import completion keeps fallback load flow when portfolio context is all-portfolios', async () => {
    setupDefaultApiMocks();
    mockPortfolioContext('all', { isAllPortfolios: true, portfolios: [] });

    render(
      <MemoryRouter>
        <PortfolioPage />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(selectPortfolio).toHaveBeenCalledWith(portfolioA.id);
    });

    const getByIdCallsBeforeImport = mockedPortfolioApi.getById.mock.calls.length;
    const getAllCallsBeforeImport = mockedPortfolioApi.getAll.mock.calls.length;

    const user = userEvent.setup();
    await user.click(screen.getByTestId('mock-stock-import-complete'));

    await waitFor(() => {
      expect(mockedPortfolioApi.getAll.mock.calls.length).toBeGreaterThan(getAllCallsBeforeImport);
    });

    expect(mockedPortfolioApi.getById.mock.calls.length).toBe(getByIdCallsBeforeImport);
  });

  it('keeps composite-key prices internally and avoids ambiguous ticker payload to API', async () => {
    setupDefaultApiMocks();
    mockPortfolioContext(portfolioA.id);
    seedCompositeQuoteCacheForDuplicateTicker();

    mockedPortfolioApi.getSummary.mockImplementation(async (portfolioId: string, currentPrices?: Record<string, { price: number; exchangeRate: number }>) => {
      if (currentPrices && Object.keys(currentPrices).length > 0) {
        return buildSummaryWithDuplicateTickerAcrossMarkets(portfolioA);
      }
      if (portfolioId === portfolioB.id) {
        return buildEmptySummary(portfolioB);
      }
      return buildSummaryWithDuplicateTickerAcrossMarkets(portfolioA);
    });

    render(
      <MemoryRouter>
        <PortfolioPage />
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(mockedPortfolioApi.getById).toHaveBeenCalledWith(portfolioA.id);
    });

    await waitFor(() => {
      expect(mockedPortfolioApi.getSummary).toHaveBeenCalledWith(portfolioA.id);
    });

    expect(mockedPortfolioApi.calculateXirr).not.toHaveBeenCalled();

    const payloadCalls = mockedPortfolioApi.getSummary.mock.calls.filter(([, currentPrices]) => Boolean(currentPrices));
    expect(payloadCalls.length).toBe(0);
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

    expect(localStorage.getItem('quote_cache_AAPL_2')).not.toBeNull();
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

    expect(localStorage.getItem('quote_cache_AAPL_2')).not.toBeNull();
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

    expect(localStorage.getItem('quote_cache_AAPL_2')).not.toBeNull();
  });

  it('keeps market-scoped quote cache key untouched after transaction mutation', async () => {
    seedHistoricalPerformanceCache();
    localStorage.setItem('quote_cache_ABC_2', JSON.stringify({ quote: { price: 100 } }));

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
    await user.click(screen.getByTestId('mock-transaction-form-submit'));

    await waitFor(() => {
      expect(mockedTransactionApi.create).toHaveBeenCalledTimes(1);
      expect(invalidateSharedCaches).toHaveBeenCalledTimes(1);
      expect(localStorage.getItem('perf_years_portfolio-a')).toBeNull();
      expect(localStorage.getItem('perf_data_portfolio-a_2025')).toBeNull();
    });

    expect(localStorage.getItem('quote_cache_ABC_2')).not.toBeNull();
  });
});
