import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { CurrencyTransactionForm } from '../components/currency/CurrencyTransactionForm';
import { TransactionForm } from '../components/transactions/TransactionForm';
import CurrencyDetail from '../pages/CurrencyDetail';
import { generateCurrencyTransactionsCsv } from '../services/csvExport';
import {
  CurrencyTransactionType,
  type CurrencyLedgerSummary,
  type CurrencyTransaction,
  type Portfolio,
} from '../types';

vi.mock('react-router-dom', () => ({
  useParams: vi.fn(() => ({ id: 'ledger-1' })),
  useNavigate: vi.fn(() => vi.fn()),
}));

vi.mock('../contexts/LedgerContext', () => ({
  useLedger: vi.fn(),
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
  mockedStockPriceApi.getQuote.mockResolvedValue(null);
});

describe('currency transaction type display naming consistency', () => {
  it('shows redesigned InitialBalance wording in CurrencyTransactionForm options', () => {
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
});
