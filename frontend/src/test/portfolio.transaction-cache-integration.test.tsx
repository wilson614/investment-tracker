import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { PortfolioPage } from '../pages/Portfolio';
import { PortfolioProvider } from '../contexts/PortfolioContext';
import { portfolioApi, transactionApi } from '../services/api';
import { Currency, StockMarket, TransactionType } from '../types';
import type {
  CreateStockTransactionRequest,
  Portfolio,
  PortfolioSummary,
  StockTransaction,
  XirrResult,
} from '../types';

vi.mock('../hooks/useAuth', () => ({
  useAuth: () => ({
    user: { id: 'user-1' },
  }),
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
  TransactionForm: ({
    onSubmit,
    initialData,
  }: {
    onSubmit: (data: CreateStockTransactionRequest) => Promise<void>;
    initialData?: StockTransaction;
  }) => (
    <button
      type="button"
      data-testid="trigger-create-transaction"
      onClick={() => {
        void onSubmit({
          portfolioId: initialData?.portfolioId ?? 'portfolio-a',
          transactionDate: '2026-01-02',
          ticker: initialData?.ticker ?? 'AAPL',
          transactionType: TransactionType.Buy,
          shares: initialData?.shares ?? 1,
          pricePerShare: 100,
          fees: 1,
          market: initialData?.market ?? StockMarket.US,
          currency: initialData?.currency ?? Currency.USD,
        });
      }}
    >
      trigger-create-transaction
    </button>
  ),
}));

vi.mock('../components/transactions/TransactionList', () => ({
  TransactionList: ({
    transactions,
    onEdit,
  }: {
    transactions: StockTransaction[];
    onEdit?: (transaction: StockTransaction) => void;
  }) => {
    if (!onEdit || transactions.length === 0) {
      return null;
    }

    return (
      <button
        type="button"
        data-testid="trigger-edit-transaction"
        onClick={() => {
          onEdit(transactions[0]);
        }}
      >
        trigger-edit-transaction
      </button>
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
  ConfirmationModal: () => null,
}));

vi.mock('../services/csvExport', () => ({
  exportTransactionsToCsv: vi.fn(),
}));

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

const emptySummary: PortfolioSummary = {
  portfolio: portfolioA,
  positions: [],
  totalCostHome: 0,
  totalValueHome: 0,
  totalUnrealizedPnlHome: 0,
  totalUnrealizedPnlPercentage: 0,
};

const emptyXirr: XirrResult = {
  xirr: null,
  xirrPercentage: null,
  cashFlowCount: 0,
  asOfDate: nowIso,
  earliestTransactionDate: null,
  missingExchangeRates: null,
};

const createdTransaction: StockTransaction = {
  id: 'tx-created',
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
};

const existingTransaction: StockTransaction = {
  ...createdTransaction,
  id: 'tx-existing',
};

const updatedTransaction: StockTransaction = {
  ...existingTransaction,
  pricePerShare: 110,
  totalCostSource: 111,
  totalCostHome: 3330,
  updatedAt: '2026-01-03T00:00:00.000Z',
};

function createWrapper(queryClient: QueryClient) {
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      <PortfolioProvider>
        <MemoryRouter>{children}</MemoryRouter>
      </PortfolioProvider>
    </QueryClientProvider>
  );
}

describe('PortfolioPage transaction cache invalidation integration', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.clearAllMocks();

    localStorage.setItem('selected_portfolio_id', portfolioA.id);
    localStorage.setItem('perf_years_portfolio-a', JSON.stringify({ years: [2025] }));
    localStorage.setItem('perf_data_portfolio-a_2025', JSON.stringify({ year: 2025 }));
    localStorage.setItem('quote_cache_AAPL', JSON.stringify({ quote: { price: 100 } }));

    mockedPortfolioApi.getAll.mockResolvedValue([portfolioA]);
    mockedPortfolioApi.getById.mockResolvedValue(portfolioA);
    mockedPortfolioApi.getSummary.mockResolvedValue(emptySummary);
    mockedPortfolioApi.calculateXirr.mockResolvedValue(emptyXirr);

    mockedTransactionApi.getByPortfolio.mockResolvedValue([] as StockTransaction[]);
    mockedTransactionApi.create.mockResolvedValue(createdTransaction);
    mockedTransactionApi.update.mockResolvedValue(updatedTransaction);
  });

  it('clears perf_years_/perf_data_ localStorage after create transaction with real PortfolioProvider', async () => {
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });

    render(<PortfolioPage />, { wrapper: createWrapper(queryClient) });

    await waitFor(() => {
      expect(mockedPortfolioApi.getById).toHaveBeenCalledWith(portfolioA.id);
    });

    const user = userEvent.setup();
    await user.click(screen.getByTestId('portfolio-add-transaction'));
    expect(localStorage.getItem('perf_years_portfolio-a')).not.toBeNull();
    expect(localStorage.getItem('perf_data_portfolio-a_2025')).not.toBeNull();
    await user.click(screen.getByTestId('trigger-create-transaction'));

    await waitFor(() => {
      expect(mockedTransactionApi.create).toHaveBeenCalledTimes(1);
      expect(localStorage.getItem('perf_years_portfolio-a')).toBeNull();
      expect(localStorage.getItem('perf_data_portfolio-a_2025')).toBeNull();
    });

    expect(localStorage.getItem('quote_cache_AAPL')).not.toBeNull();
  });

  it('clears perf_years_/perf_data_ localStorage after update transaction via onEdit with real PortfolioProvider', async () => {
    mockedTransactionApi.getByPortfolio.mockResolvedValue([existingTransaction]);

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });

    render(<PortfolioPage />, { wrapper: createWrapper(queryClient) });

    await waitFor(() => {
      expect(mockedPortfolioApi.getById).toHaveBeenCalledWith(portfolioA.id);
      expect(screen.getByTestId('trigger-edit-transaction')).toBeInTheDocument();
    });

    const user = userEvent.setup();
    await user.click(screen.getByTestId('trigger-edit-transaction'));
    expect(localStorage.getItem('perf_years_portfolio-a')).not.toBeNull();
    expect(localStorage.getItem('perf_data_portfolio-a_2025')).not.toBeNull();
    await user.click(screen.getByTestId('trigger-create-transaction'));

    await waitFor(() => {
      expect(mockedTransactionApi.update).toHaveBeenCalledTimes(1);
      expect(mockedTransactionApi.update).toHaveBeenCalledWith(
        existingTransaction.id,
        expect.objectContaining({
          ticker: 'AAPL',
          transactionType: TransactionType.Buy,
          market: StockMarket.US,
          currency: Currency.USD,
        })
      );
      expect(mockedTransactionApi.create).not.toHaveBeenCalled();
      expect(localStorage.getItem('perf_years_portfolio-a')).toBeNull();
      expect(localStorage.getItem('perf_data_portfolio-a_2025')).toBeNull();
    });

    expect(localStorage.getItem('quote_cache_AAPL')).not.toBeNull();
  });
});
