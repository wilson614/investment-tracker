import { describe, it, expect } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { PerformanceMetrics } from '../components/portfolio/PerformanceMetrics';
import { StockMarket, type PortfolioSummary, type XirrResult } from '../types';

const nowIso = '2026-01-01T00:00:00.000Z';

function buildSummary(): PortfolioSummary {
  return {
    portfolio: {
      id: 'portfolio-1',
      baseCurrency: 'USD',
      homeCurrency: 'TWD',
      isActive: true,
      displayName: '測試投組',
      boundCurrencyLedgerId: 'ledger-1',
      createdAt: nowIso,
      updatedAt: nowIso,
    },
    positions: [
      {
        ticker: 'AAPL',
        totalShares: 10,
        totalCostHome: 30000,
        totalCostSource: 1000,
        averageCostPerShareHome: 3000,
        averageCostPerShareSource: 100,
        currentPrice: 120,
        currentExchangeRate: 31,
        currentValueHome: 37200,
        currentValueSource: 1200,
        unrealizedPnlHome: 7200,
        unrealizedPnlPercentage: 24,
        unrealizedPnlSource: 200,
        unrealizedPnlSourcePercentage: 20,
        market: StockMarket.US,
        currency: 'USD',
      },
    ],
    totalCostHome: 30000,
    totalValueHome: 37200,
    totalUnrealizedPnlHome: 7200,
    totalUnrealizedPnlPercentage: 24,
  };
}

function buildXirrResult(overrides: Partial<XirrResult> = {}): XirrResult {
  return {
    xirr: 0.1234,
    xirrPercentage: 12.34,
    cashFlowCount: 3,
    asOfDate: '2026-01-01',
    earliestTransactionDate: '2025-01-01',
    missingExchangeRates: null,
    ...overrides,
  };
}

describe('PerformanceMetrics XIRR display consistency', () => {
  it('shows explicit unavailable wording instead of dash when XIRR is unavailable', () => {
    render(
      <PerformanceMetrics
        summary={buildSummary()}
        xirrResult={buildXirrResult({ xirr: null, xirrPercentage: null })}
      />
    );

    const xirrCard = screen.getByText('年化報酬 (XIRR)').closest('.metric-card') as HTMLElement | null;
    expect(xirrCard).not.toBeNull();

    expect(within(xirrCard as HTMLElement).getByText('資料不足不顯示')).toBeInTheDocument();
    expect(within(xirrCard as HTMLElement).getByText('因交易筆數或資料期間不足，暫無法可靠計算 XIRR。')).toBeInTheDocument();

    const tooltipTrigger = within(xirrCard as HTMLElement).getByRole('button', {
      name: 'XIRR 無法計算說明',
    });
    tooltipTrigger.focus();
    expect(tooltipTrigger).toHaveFocus();
    expect(tooltipTrigger).toHaveAttribute('aria-describedby');

    const tooltip = within(xirrCard as HTMLElement).getByRole('tooltip');
    expect(tooltip).toHaveAttribute('id', tooltipTrigger.getAttribute('aria-describedby') ?? '');
    expect(tooltip.className).toContain('group-hover:block');
    expect(tooltip.className).toContain('group-focus-within:block');

    expect(within(xirrCard as HTMLElement).queryByText('-', { exact: true })).not.toBeInTheDocument();
    expect(within(xirrCard as HTMLElement).queryByText('-')).not.toBeInTheDocument();
  });

  it('shows low-confidence wording in warning color and keeps XIRR value', () => {
    render(
      <PerformanceMetrics
        summary={buildSummary()}
        xirrResult={buildXirrResult({
          xirrPercentage: 8.88,
          earliestTransactionDate: '2025-12-01',
          asOfDate: '2026-01-01',
        })}
      />
    );

    const xirrCard = screen.getByText('年化報酬 (XIRR)').closest('.metric-card') as HTMLElement | null;
    expect(xirrCard).not.toBeNull();

    expect(within(xirrCard as HTMLElement).getByText('+8.88%')).toBeInTheDocument();

    const warningText = within(xirrCard as HTMLElement).getByText('資料期間較短，年化報酬僅供參考');
    expect(warningText).toBeInTheDocument();
    expect(warningText.className).toContain('text-[var(--color-warning)]');
  });

  it('shows transaction count only when XIRR is available', () => {
    render(
      <PerformanceMetrics
        summary={buildSummary()}
        xirrResult={buildXirrResult({ xirr: null, xirrPercentage: null, cashFlowCount: 4 })}
      />
    );

    const xirrCard = screen.getByText('年化報酬 (XIRR)').closest('.metric-card') as HTMLElement | null;
    expect(xirrCard).not.toBeNull();

    expect(within(xirrCard as HTMLElement).queryByText('基於 3 筆交易計算')).not.toBeInTheDocument();
  });
});
