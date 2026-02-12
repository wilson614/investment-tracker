import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { StockMarket } from '../types';
import type {
  CapeData,
  MarketYtdComparison,
  MonthlyNetWorthHistory,
  Portfolio,
  PortfolioSummary,
  XirrResult,
} from '../types';

vi.mock('../components/dashboard', () => ({
  MarketContext: () => <div data-testid="market-context" />,
  MarketYtdSection: () => <div data-testid="market-ytd" />,
  HistoricalValueChart: () => <div data-testid="historical-chart" />,
}));

vi.mock('../components/charts', () => ({
  AssetAllocationPieChart: () => <div data-testid="asset-allocation-chart" />,
}));

vi.mock('../components/common/XirrWarningBadge', () => ({
  XirrWarningBadge: () => null,
}));

vi.mock('../components/common/SkeletonLoader', () => ({
  Skeleton: () => <div data-testid="skeleton" />,
}));

vi.mock('../components/portfolio/PortfolioSelector', () => ({
  PortfolioSelector: () => <div data-testid="portfolio-selector" />,
}));

vi.mock('../contexts/PortfolioContext', () => ({
  usePortfolio: vi.fn(),
}));

vi.mock('../services/api', () => ({
  portfolioApi: {
    getSummary: vi.fn(),
    getMonthlyNetWorth: vi.fn(),
    calculateAggregateXirr: vi.fn(),
    getById: vi.fn(),
    calculateXirr: vi.fn(),
  },
  transactionApi: {
    getByPortfolio: vi.fn(),
  },
  stockPriceApi: {
    getQuoteWithRate: vi.fn(),
  },
  marketDataApi: {
    getEuronextQuoteByTicker: vi.fn(),
  },
}));

vi.mock('../services/capeApi', () => ({
  refreshCapeData: vi.fn(),
}));

vi.mock('../services/ytdApi', () => ({
  refreshYtdData: vi.fn(),
}));

import { DashboardPage } from '../pages/Dashboard';
import { usePortfolio } from '../contexts/PortfolioContext';
import { portfolioApi, transactionApi, stockPriceApi } from '../services/api';
import { refreshCapeData } from '../services/capeApi';
import { refreshYtdData } from '../services/ytdApi';

const mockedUsePortfolio = vi.mocked(usePortfolio);
const mockedPortfolioApi = vi.mocked(portfolioApi, { deep: true });
const mockedTransactionApi = vi.mocked(transactionApi, { deep: true });
const mockedStockPriceApi = vi.mocked(stockPriceApi, { deep: true });
const mockedRefreshCapeData = vi.mocked(refreshCapeData);
const mockedRefreshYtdData = vi.mocked(refreshYtdData);

const nowIso = '2026-01-01T00:00:00.000Z';

const portfolios: Portfolio[] = [
  {
    id: 'p1',
    baseCurrency: 'USD',
    homeCurrency: 'TWD',
    isActive: true,
    displayName: 'USD Portfolio',
    boundCurrencyLedgerId: 'ledger-1',
    createdAt: nowIso,
    updatedAt: nowIso,
  },
  {
    id: 'p2',
    baseCurrency: 'TWD',
    homeCurrency: 'TWD',
    isActive: true,
    displayName: 'TWD Portfolio',
    boundCurrencyLedgerId: 'ledger-2',
    createdAt: nowIso,
    updatedAt: nowIso,
  },
];

const createSummary = (portfolio: Portfolio): PortfolioSummary => ({
  portfolio,
  positions: [
    {
      ticker: 'AAPL',
      totalShares: 10,
      totalCostHome: 32000,
      totalCostSource: 1000,
      averageCostPerShareHome: 3200,
      averageCostPerShareSource: 100,
      currentPrice: 120,
      currentExchangeRate: 31,
      currentValueHome: 37200,
      currentValueSource: 1200,
      unrealizedPnlHome: 5200,
      unrealizedPnlPercentage: 16.25,
      unrealizedPnlSource: 200,
      unrealizedPnlSourcePercentage: 20,
      market: StockMarket.US,
      currency: 'USD',
    },
  ],
  totalCostHome: 32000,
  totalValueHome: 37200,
  totalUnrealizedPnlHome: 5200,
  totalUnrealizedPnlPercentage: 16.25,
});

const summariesById: Record<string, PortfolioSummary> = {
  p1: createSummary(portfolios[0]),
  p2: createSummary(portfolios[1]),
};

const emptyHistory: MonthlyNetWorthHistory = {
  data: [],
  currency: 'TWD',
  totalMonths: 0,
  dataSource: 'test',
};

const aggregateXirr: XirrResult = {
  xirr: 0.12,
  xirrPercentage: 12,
  cashFlowCount: 2,
  asOfDate: nowIso,
  earliestTransactionDate: '2025-01-01',
  missingExchangeRates: null,
};

const mockCapeData: CapeData = {
  date: '2026-01-01',
  items: [],
  fetchedAt: nowIso,
};

const mockYtdData: MarketYtdComparison = {
  year: 2026,
  benchmarks: [],
  generatedAt: nowIso,
};

function seedAaplQuoteCache() {
  localStorage.setItem(
    'quote_cache_AAPL_2',
    JSON.stringify({
      quote: {
        symbol: 'AAPL',
        name: 'Apple Inc.',
        price: 123,
        market: StockMarket.US,
        source: 'test',
        fetchedAt: nowIso,
        exchangeRate: 31,
        exchangeRatePair: 'USD/TWD',
      },
      updatedAt: nowIso,
      market: StockMarket.US,
    })
  );
}

function setupDashboardMocks(params: {
  currentPortfolioId: string;
  isAllPortfolios: boolean;
}) {
  const { currentPortfolioId, isAllPortfolios } = params;

  mockedUsePortfolio.mockReturnValue({
    currentPortfolio: portfolios.find((p) => p.id === currentPortfolioId) ?? null,
    currentPortfolioId,
    isAllPortfolios,
    portfolios,
    isLoading: false,
    selectPortfolio: vi.fn(),
    refreshPortfolios: vi.fn().mockResolvedValue(undefined),
    clearPerformanceState: vi.fn(),
    performanceVersion: 0,
  });

  mockedPortfolioApi.getSummary.mockImplementation(async (portfolioId: string) => {
    const summary = summariesById[portfolioId];
    if (!summary) {
      throw new Error(`Unknown portfolio id: ${portfolioId}`);
    }
    return summary;
  });

  mockedPortfolioApi.getMonthlyNetWorth.mockResolvedValue(emptyHistory);
  mockedPortfolioApi.calculateAggregateXirr.mockResolvedValue(aggregateXirr);
  mockedPortfolioApi.getById.mockResolvedValue(portfolios[0]);
  mockedPortfolioApi.calculateXirr.mockResolvedValue(aggregateXirr);

  mockedTransactionApi.getByPortfolio.mockResolvedValue([]);
  mockedStockPriceApi.getQuoteWithRate.mockResolvedValue(
    null as unknown as Awaited<ReturnType<(typeof stockPriceApi)['getQuoteWithRate']>>
  );

  mockedRefreshCapeData.mockResolvedValue(mockCapeData);
  mockedRefreshYtdData.mockResolvedValue(mockYtdData);
}

describe('DashboardPage aggregate fixed behavior', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.clearAllMocks();
    seedAaplQuoteCache();
  });

  it.each([
    {
      name: 'context currently selects a specific portfolio',
      currentPortfolioId: 'p1',
      isAllPortfolios: false,
    },
    {
      name: 'context currently selects all portfolios',
      currentPortfolioId: 'all',
      isAllPortfolios: true,
    },
  ])('always runs aggregate flow when $name', async ({ currentPortfolioId, isAllPortfolios }) => {
    setupDashboardMocks({ currentPortfolioId, isAllPortfolios });

    render(<DashboardPage />);

    await screen.findByRole('heading', { name: '儀表板' });

    await waitFor(() => {
      expect(mockedPortfolioApi.calculateAggregateXirr).toHaveBeenCalled();
    });

    const calledPortfolioIds = new Set(
      mockedPortfolioApi.getSummary.mock.calls.map(([portfolioId]) => portfolioId)
    );

    expect(calledPortfolioIds).toEqual(new Set(['p1', 'p2']));
    expect(mockedPortfolioApi.calculateAggregateXirr).toHaveBeenCalledWith(
      expect.objectContaining({
        currentPrices: expect.objectContaining({
          AAPL: expect.objectContaining({ price: 123, exchangeRate: 31 }),
        }),
      })
    );

    expect(mockedPortfolioApi.getById).not.toHaveBeenCalled();
    expect(mockedPortfolioApi.calculateXirr).not.toHaveBeenCalled();
    expect(screen.queryByTestId('portfolio-selector')).not.toBeInTheDocument();
    expect(screen.getByText('各投資組合市值貢獻')).toBeInTheDocument();
  });
});
