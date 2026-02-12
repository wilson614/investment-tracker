import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PortfolioSelector } from '../components/portfolio/PortfolioSelector';
import { usePortfolio } from '../contexts/PortfolioContext';
import type { Portfolio } from '../types';

vi.mock('../contexts/PortfolioContext', () => ({
  usePortfolio: vi.fn(),
}));

const mockedUsePortfolio = vi.mocked(usePortfolio);

const mockPortfolios: Portfolio[] = [
  {
    id: 'portfolio-usd',
    baseCurrency: 'USD',
    homeCurrency: 'TWD',
    isActive: true,
    boundCurrencyLedgerId: 'ledger-usd',
    createdAt: '2025-01-01T00:00:00Z',
    updatedAt: '2025-01-01T00:00:00Z',
  },
  {
    id: 'portfolio-twd',
    baseCurrency: 'TWD',
    homeCurrency: 'TWD',
    isActive: true,
    boundCurrencyLedgerId: 'ledger-twd',
    createdAt: '2025-01-02T00:00:00Z',
    updatedAt: '2025-01-02T00:00:00Z',
  },
];

afterEach(() => {
  vi.clearAllMocks();
});

function mockPortfolioContext(options?: {
  currentPortfolioId?: string;
  isAllPortfolios?: boolean;
}) {
  const currentPortfolioId = options?.currentPortfolioId ?? 'portfolio-usd';
  const isAllPortfolios = options?.isAllPortfolios ?? false;
  const currentPortfolio =
    currentPortfolioId === 'all'
      ? null
      : mockPortfolios.find((portfolio) => portfolio.id === currentPortfolioId) ?? mockPortfolios[0];

  const selectPortfolio = vi.fn();

  mockedUsePortfolio.mockReturnValue({
    portfolios: mockPortfolios,
    currentPortfolioId,
    currentPortfolio,
    isAllPortfolios,
    selectPortfolio,
    isLoading: false,
    refreshPortfolios: vi.fn(),
    clearPerformanceState: vi.fn(),
    performanceVersion: 0,
  });

  return { selectPortfolio };
}

describe('PortfolioSelector option visibility', () => {
  it('hides "所有投資組合" option when includeAllOption is false and allows switching portfolios', async () => {
    const user = userEvent.setup();
    const { selectPortfolio } = mockPortfolioContext({
      currentPortfolioId: 'portfolio-usd',
      isAllPortfolios: false,
    });

    render(<PortfolioSelector onCreateNew={vi.fn()} includeAllOption={false} />);

    await user.click(screen.getByRole('button', { name: '美金' }));

    expect(screen.queryByText('所有投資組合')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: '台幣' }));
    expect(selectPortfolio).toHaveBeenCalledWith('portfolio-twd');
  });

  it('shows "所有投資組合" option by default', async () => {
    const user = userEvent.setup();
    mockPortfolioContext({
      currentPortfolioId: 'portfolio-usd',
      isAllPortfolios: false,
    });

    render(<PortfolioSelector onCreateNew={vi.fn()} />);

    await user.click(screen.getByRole('button', { name: '美金' }));

    expect(screen.getByRole('button', { name: '所有投資組合' })).toBeInTheDocument();
  });
});
