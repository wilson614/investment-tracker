import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { CurrencyTransactionForm } from '../components/currency/CurrencyTransactionForm';
import { TransactionForm } from '../components/transactions/TransactionForm';
import { TransactionList } from '../components/transactions/TransactionList';
import CurrencyDetail from '../pages/CurrencyDetail';
import { generateCurrencyTransactionsCsv } from '../services/csvExport';
import {
  CurrencyTransactionType,
  StockTransactionTopUpType,
  TransactionType as TransactionTypeEnum,
  StockMarket as StockMarketEnum,
  Currency as CurrencyEnum,
  type CurrencyLedgerSummary,
  type CurrencyTransaction,
  type Portfolio,
  type StockTransaction,
} from '../types';

vi.mock('react-router-dom', () => ({
  useParams: vi.fn(() => ({ id: 'ledger-1' })),
  useNavigate: vi.fn(() => vi.fn()),
}));

vi.mock('../contexts/LedgerContext', () => ({
  useLedger: vi.fn(),
}));

vi.mock('../contexts/PortfolioContext', () => ({
  usePortfolio: vi.fn(() => ({
    invalidateSharedCaches: vi.fn(),
  })),
}));

vi.mock('../components/ledger/LedgerSelector', () => ({
  LedgerSelector: () => <div data-testid="ledger-selector" />,
}));

vi.mock('../components/import', () => ({
  CurrencyImportButton: () => null,
}));

vi.mock('../components/common', () => ({
  FileDropdown: () => null,
}));

vi.mock('../components/modals/ConfirmationModal', () => ({
  ConfirmationModal: () => null,
}));

vi.mock('../services/api', () => ({
  currencyLedgerApi: {
    getAll: vi.fn(),
    getById: vi.fn(),
    getExchangeRatePreview: vi.fn(),
  },
  currencyTransactionApi: {
    getByLedger: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    delete: vi.fn(),
  },
  stockPriceApi: {
    getExchangeRate: vi.fn(),
    getQuote: vi.fn(),
  },
}));

import { currencyLedgerApi, currencyTransactionApi, stockPriceApi } from '../services/api';
import { useLedger } from '../contexts/LedgerContext';

const mockedCurrencyLedgerApi = vi.mocked(currencyLedgerApi, { deep: true });
const mockedCurrencyTransactionApi = vi.mocked(currencyTransactionApi, { deep: true });
const mockedStockPriceApi = vi.mocked(stockPriceApi, { deep: true });
const mockedUseLedger = vi.mocked(useLedger);

const nowIso = '2026-01-01T00:00:00.000Z';

const mockPortfolio: Portfolio = {
  id: 'portfolio-1',
  baseCurrency: 'USD',
  homeCurrency: 'TWD',
  isActive: true,
  displayName: 'USD Portfolio',
  boundCurrencyLedgerId: 'ledger-1',
  createdAt: nowIso,
  updatedAt: nowIso,
};

const mockLedgerSummary: CurrencyLedgerSummary = {
  ledger: {
    id: 'ledger-1',
    currencyCode: 'USD',
    name: 'USD Ledger',
    homeCurrency: 'TWD',
    isActive: true,
    createdAt: nowIso,
    updatedAt: nowIso,
  },
  balance: 0,
  averageExchangeRate: 31.5,
  totalExchanged: 0,
  totalSpentOnStocks: 0,
  totalInterest: 0,
  totalCost: 0,
  realizedPnl: 0,
  recentTransactions: [],
};

function createCurrencyTransaction(overrides: Partial<CurrencyTransaction> = {}): CurrencyTransaction {
  return {
    id: 'tx-1',
    currencyLedgerId: 'ledger-1',
    transactionDate: '2026-01-10T00:00:00.000Z',
    transactionType: CurrencyTransactionType.InitialBalance,
    foreignAmount: 100,
    homeAmount: 3150,
    exchangeRate: 31.5,
    relatedStockTransactionId: undefined,
    notes: '',
    createdAt: nowIso,
    updatedAt: nowIso,
    ...overrides,
  };
}

function createStockTransaction(overrides: Partial<StockTransaction> = {}): StockTransaction {
  return {
    id: 'stock-tx-1',
    portfolioId: mockPortfolio.id,
    transactionDate: '2026-01-10T00:00:00.000Z',
    ticker: '2330',
    transactionType: TransactionTypeEnum.Buy,
    shares: 10,
    pricePerShare: 100,
    exchangeRate: 1,
    fees: 1,
    currencyLedgerId: 'ledger-1',
    notes: '',
    totalCostSource: 1001,
    totalCostHome: 1001,
    hasExchangeRate: true,
    realizedPnlHome: undefined,
    createdAt: nowIso,
    updatedAt: nowIso,
    adjustedShares: undefined,
    adjustedPricePerShare: undefined,
    splitRatio: 1,
    hasSplitAdjustment: false,
    market: StockMarketEnum.TW,
    currency: CurrencyEnum.TWD,
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();

  mockedUseLedger.mockReturnValue({
    currentLedger: null,
    currentLedgerId: null,
    ledgers: [],
    isLoading: false,
    selectLedger: vi.fn(),
    refreshLedgers: vi.fn(async () => undefined),
  });

  mockedCurrencyLedgerApi.getAll.mockResolvedValue([mockLedgerSummary]);
  mockedCurrencyLedgerApi.getById.mockResolvedValue(mockLedgerSummary);
  mockedCurrencyLedgerApi.getExchangeRatePreview.mockResolvedValue({
    rate: 31.5,
    source: 'market',
  });

  mockedCurrencyTransactionApi.getByLedger.mockResolvedValue([]);
  mockedCurrencyTransactionApi.create.mockResolvedValue(createCurrencyTransaction());
  mockedCurrencyTransactionApi.update.mockResolvedValue(createCurrencyTransaction());
  mockedCurrencyTransactionApi.delete.mockResolvedValue(undefined);

  mockedStockPriceApi.getExchangeRate.mockResolvedValue({
    fromCurrency: 'USD',
    toCurrency: 'TWD',
    rate: 31.5,
    source: 'test',
    fetchedAt: nowIso,
  });
  mockedStockPriceApi.getQuote.mockResolvedValue(
    null as unknown as Awaited<ReturnType<(typeof stockPriceApi)['getQuote']>>
  );
});

describe('currency transaction type display naming consistency', () => {
  it('shows redesigned InitialBalance wording and hides Spend in new CurrencyTransactionForm', () => {
    render(
      <CurrencyTransactionForm
        ledgerId="ledger-1"
        currencyCode="USD"
        onSubmit={vi.fn(async () => undefined)}
        onCancel={vi.fn()}
      />
    );

    expect(screen.getByRole('option', { name: '轉入餘額' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: '轉入本金' })).not.toBeInTheDocument();
    expect(screen.queryByRole('option', { name: '消費支出' })).not.toBeInTheDocument();
    expect(screen.getByText('提示：股票買入的帳本支出會由投組交易自動建立，無需手動新增「消費支出」。')).toBeInTheDocument();
  });

  it('keeps legacy Spend visible when editing an existing Spend transaction', () => {
    render(
      <CurrencyTransactionForm
        ledgerId="ledger-1"
        currencyCode="USD"
        initialData={createCurrencyTransaction({
          transactionType: CurrencyTransactionType.Spend,
        })}
        onSubmit={vi.fn(async () => undefined)}
        onCancel={vi.fn()}
      />
    );

    const transactionTypeSelect = screen.getByRole('combobox') as HTMLSelectElement;
    expect(transactionTypeSelect.value).toBe(String(CurrencyTransactionType.Spend));
    expect(screen.getByRole('option', { name: '消費支出（舊資料）' })).toBeInTheDocument();
    expect(screen.queryByText('提示：股票買入的帳本支出會由投組交易自動建立，無需手動新增「消費支出」。')).not.toBeInTheDocument();
  });

  it('shows redesigned InitialBalance wording in stock top-up transaction options', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn(async () => undefined);

    const { container } = render(
      <TransactionForm
        portfolioId={mockPortfolio.id}
        portfolio={mockPortfolio}
        onSubmit={onSubmit}
      />
    );

    const tickerInput = container.querySelector('input[name="ticker"]') as HTMLInputElement;
    const sharesInput = container.querySelector('input[name="shares"]') as HTMLInputElement;
    const priceInput = container.querySelector('input[name="pricePerShare"]') as HTMLInputElement;

    fireEvent.change(tickerInput, { target: { value: 'AAPL' } });
    fireEvent.change(sharesInput, { target: { value: '1' } });
    fireEvent.change(priceInput, { target: { value: '100' } });

    await user.click(screen.getByRole('button', { name: '新增交易' }));

    expect(await screen.findByText('帳本餘額不足')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /補足餘額/ }));

    expect(await screen.findByRole('option', { name: '轉入餘額' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: '轉入本金' })).not.toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('uses backend-aligned top-up enum values when submitting stock buy with insufficient balance', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn(async () => undefined);

    const { container } = render(
      <TransactionForm
        portfolioId={mockPortfolio.id}
        portfolio={mockPortfolio}
        onSubmit={onSubmit}
      />
    );

    const tickerInput = container.querySelector('input[name="ticker"]') as HTMLInputElement;
    const sharesInput = container.querySelector('input[name="shares"]') as HTMLInputElement;
    const priceInput = container.querySelector('input[name="pricePerShare"]') as HTMLInputElement;

    fireEvent.change(tickerInput, { target: { value: 'AAPL' } });
    fireEvent.change(sharesInput, { target: { value: '1' } });
    fireEvent.change(priceInput, { target: { value: '100' } });

    await user.click(screen.getByRole('button', { name: '新增交易' }));
    await screen.findByText('帳本餘額不足');

    await user.click(screen.getByRole('button', { name: /補足餘額/ }));

    const topUpSelect = screen
      .getByRole('option', { name: '選擇交易類型' })
      .closest('select') as HTMLSelectElement | null;
    expect(topUpSelect).not.toBeNull();

    await user.selectOptions(topUpSelect as HTMLSelectElement, String(StockTransactionTopUpType.Deposit));

    await user.click(screen.getByRole('button', { name: '確認' }));

    expect(onSubmit).toHaveBeenCalledTimes(1);
    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({
        balanceAction: 2,
        topUpTransactionType: StockTransactionTopUpType.Deposit,
      })
    );
  });

  it('hides ExchangeBuy top-up option in stock top-up selector for both non-TWD and TWD bound-ledger portfolios', async () => {
    const user = userEvent.setup();

    const nonTwdSubmit = vi.fn(async () => undefined);
    const { container: nonTwdContainer, unmount } = render(
      <TransactionForm
        portfolioId={mockPortfolio.id}
        portfolio={mockPortfolio}
        onSubmit={nonTwdSubmit}
      />
    );

    const nonTwdTicker = nonTwdContainer.querySelector('input[name="ticker"]') as HTMLInputElement;
    const nonTwdShares = nonTwdContainer.querySelector('input[name="shares"]') as HTMLInputElement;
    const nonTwdPrice = nonTwdContainer.querySelector('input[name="pricePerShare"]') as HTMLInputElement;

    fireEvent.change(nonTwdTicker, { target: { value: 'AAPL' } });
    fireEvent.change(nonTwdShares, { target: { value: '1' } });
    fireEvent.change(nonTwdPrice, { target: { value: '100' } });

    await user.click(screen.getByRole('button', { name: '新增交易' }));
    await screen.findByText('帳本餘額不足');
    await user.click(screen.getByRole('button', { name: /補足餘額/ }));

    expect(screen.queryByRole('option', { name: '換匯買入' })).not.toBeInTheDocument();

    unmount();

    const twdPortfolio: Portfolio = {
      ...mockPortfolio,
      id: 'portfolio-twd',
      baseCurrency: 'USD',
      boundCurrencyLedgerId: 'ledger-twd',
    };

    mockedCurrencyLedgerApi.getAll.mockResolvedValueOnce([
      {
        ...mockLedgerSummary,
        ledger: {
          ...mockLedgerSummary.ledger,
          id: 'ledger-twd',
          currencyCode: 'TWD',
        },
      },
    ]);

    const twdSubmit = vi.fn(async () => undefined);
    const { container: twdContainer } = render(
      <TransactionForm
        portfolioId={twdPortfolio.id}
        portfolio={twdPortfolio}
        onSubmit={twdSubmit}
      />
    );

    const twdTicker = twdContainer.querySelector('input[name="ticker"]') as HTMLInputElement;
    const twdShares = twdContainer.querySelector('input[name="shares"]') as HTMLInputElement;
    const twdPrice = twdContainer.querySelector('input[name="pricePerShare"]') as HTMLInputElement;

    fireEvent.change(twdTicker, { target: { value: '2330' } });
    fireEvent.change(twdShares, { target: { value: '1' } });
    fireEvent.change(twdPrice, { target: { value: '100' } });

    await user.click(screen.getByRole('button', { name: '新增交易' }));
    await screen.findByText('帳本餘額不足');
    await user.click(screen.getByRole('button', { name: /補足餘額/ }));

    expect(screen.queryByRole('option', { name: '換匯買入' })).not.toBeInTheDocument();
  });

  it('shows redesigned wording in CurrencyDetail transaction badges', async () => {
    mockedCurrencyTransactionApi.getByLedger.mockResolvedValue([
      createCurrencyTransaction({
        id: 'tx-initial-balance',
        transactionType: CurrencyTransactionType.InitialBalance,
        foreignAmount: 200,
      }),
    ]);

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
      },
    });

    render(
      <QueryClientProvider client={queryClient}>
        <CurrencyDetail />
      </QueryClientProvider>
    );

    expect(await screen.findByText('轉入餘額')).toBeInTheDocument();
    expect(screen.queryByText('轉入本金')).not.toBeInTheDocument();
  });

  it('keeps generic Spend/OtherIncome wording for non-stock CurrencyDetail rows', async () => {
    mockedCurrencyTransactionApi.getByLedger.mockResolvedValue([
      createCurrencyTransaction({
        id: 'tx-generic-spend',
        transactionType: CurrencyTransactionType.Spend,
        foreignAmount: 120,
      }),
      createCurrencyTransaction({
        id: 'tx-generic-income',
        transactionType: CurrencyTransactionType.OtherIncome,
        foreignAmount: 80,
      }),
    ]);

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
      },
    });

    render(
      <QueryClientProvider client={queryClient}>
        <CurrencyDetail />
      </QueryClientProvider>
    );

    expect(await screen.findByText('消費支出')).toBeInTheDocument();
    expect(await screen.findByText('其他收入')).toBeInTheDocument();
    expect(screen.queryByText('股票買入')).not.toBeInTheDocument();
    expect(screen.queryByText('股票賣出')).not.toBeInTheDocument();
  });

  it('uses stock-linked labels instead of generic Spend/OtherIncome wording in CurrencyDetail', async () => {
    mockedCurrencyTransactionApi.getByLedger.mockResolvedValue([
      createCurrencyTransaction({
        id: 'tx-stock-buy',
        transactionType: CurrencyTransactionType.Spend,
        foreignAmount: 120,
        relatedStockTransactionId: 'stock-1',
      }),
      createCurrencyTransaction({
        id: 'tx-stock-sell',
        transactionType: CurrencyTransactionType.OtherIncome,
        foreignAmount: 80,
        relatedStockTransactionId: 'stock-2',
      }),
    ]);

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
      },
    });

    render(
      <QueryClientProvider client={queryClient}>
        <CurrencyDetail />
      </QueryClientProvider>
    );

    expect(await screen.findByText('股票買入')).toBeInTheDocument();
    expect(await screen.findByText('股票賣出')).toBeInTheDocument();
    expect(screen.queryByText('消費支出')).not.toBeInTheDocument();
    expect(screen.queryByText('其他收入')).not.toBeInTheDocument();
  });

  it('normalizes stock-linked transaction type before rendering CurrencyDetail labels', async () => {
    mockedCurrencyTransactionApi.getByLedger.mockResolvedValue([
      {
        ...createCurrencyTransaction({
          id: 'tx-stock-buy-normalized',
          foreignAmount: 120,
          relatedStockTransactionId: 'stock-3',
        }),
        transactionType: '  StockBuyLinked  ',
      } as unknown as CurrencyTransaction,
      {
        ...createCurrencyTransaction({
          id: 'tx-stock-sell-normalized',
          foreignAmount: 80,
          relatedStockTransactionId: 'stock-4',
        }),
        transactionType: '  6  ',
      } as unknown as CurrencyTransaction,
    ]);

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
      },
    });

    render(
      <QueryClientProvider client={queryClient}>
        <CurrencyDetail />
      </QueryClientProvider>
    );

    expect(await screen.findByText('股票買入')).toBeInTheDocument();
    expect(await screen.findByText('股票賣出')).toBeInTheDocument();
    expect(screen.queryByText('消費支出')).not.toBeInTheDocument();
    expect(screen.queryByText('其他收入')).not.toBeInTheDocument();
  });

  it('uses consistent naming in currency CSV export labels', () => {
    const csv = generateCurrencyTransactionsCsv(
      [
        createCurrencyTransaction({ transactionType: CurrencyTransactionType.ExchangeBuy }),
        createCurrencyTransaction({ transactionType: CurrencyTransactionType.ExchangeSell }),
        createCurrencyTransaction({ transactionType: CurrencyTransactionType.Interest }),
        createCurrencyTransaction({ transactionType: CurrencyTransactionType.Spend }),
        createCurrencyTransaction({ transactionType: CurrencyTransactionType.InitialBalance }),
      ],
      'USD',
      'TWD'
    );

    expect(csv).toContain('換匯買入');
    expect(csv).toContain('換匯賣出');
    expect(csv).toContain('利息收入');
    expect(csv).toContain('消費支出');
    expect(csv).toContain('轉入餘額');
    expect(csv).not.toContain('轉入本金');
  });

  it('hides exchange-rate column in TransactionList for Taiwan stock transactions with TWD and rate 1', () => {
    render(
      <TransactionList
        transactions={[
          createStockTransaction({
            id: 'tw-stock-tx',
            market: StockMarketEnum.TW,
            currency: CurrencyEnum.TWD,
            exchangeRate: 1,
          }),
        ]}
      />
    );

    expect(screen.queryByRole('columnheader', { name: '匯率' })).not.toBeInTheDocument();
  });

  it('keeps exchange-rate column visible in TransactionList for non-Taiwan stock transactions', () => {
    render(
      <TransactionList
        transactions={[
          createStockTransaction({
            id: 'us-stock-tx',
            ticker: 'AAPL',
            market: StockMarketEnum.US,
            currency: CurrencyEnum.USD,
            exchangeRate: 31.5,
          }),
        ]}
      />
    );

    expect(screen.getByRole('columnheader', { name: '匯率' })).toBeInTheDocument();
  });

  it('keeps exchange-rate column visible for TW market when currency/rate are not TWD=1', () => {
    render(
      <TransactionList
        transactions={[
          createStockTransaction({
            id: 'tw-non-twd-tx',
            market: StockMarketEnum.TW,
            currency: CurrencyEnum.USD,
            exchangeRate: 30.5,
          }),
        ]}
      />
    );

    expect(screen.getByRole('columnheader', { name: '匯率' })).toBeInTheDocument();
  });
});
