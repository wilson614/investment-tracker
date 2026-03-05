import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MissingPriceModal } from '../components/modals/MissingPriceModal';
import type { MissingPrice } from '../types';

function buildMissingPrice(
  ticker: string,
  overrides: Partial<MissingPrice> = {},
): MissingPrice {
  return {
    ticker,
    date: '2025-12-31',
    priceType: 'YearEnd',
    ...overrides,
  };
}

describe('MissingPriceModal submit payload', () => {
  it('keeps YearStart and YearEnd inputs isolated when they share the same ticker', async () => {
    const user = userEvent.setup();

    render(
      <MissingPriceModal
        isOpen={true}
        onClose={vi.fn()}
        year={2025}
        missingPrices={[
          buildMissingPrice('AAPL', { priceType: 'YearStart', date: '2024-12-31' }),
          buildMissingPrice('AAPL', { priceType: 'YearEnd', date: '2025-12-31' }),
        ]}
        onSubmit={vi.fn()}
      />,
    );

    const inputs = screen.getAllByRole('spinbutton');
    expect(inputs).toHaveLength(4);

    await user.type(inputs[0]!, '90');
    await user.type(inputs[1]!, '31');

    expect((inputs[2] as HTMLInputElement).value).toBe('');
    expect((inputs[3] as HTMLInputElement).value).toBe('');

    await user.type(inputs[2]!, '110');
    await user.type(inputs[3]!, '32');

    expect((inputs[0] as HTMLInputElement).value).toBe('90');
    expect((inputs[1] as HTMLInputElement).value).toBe('31');
    expect((inputs[2] as HTMLInputElement).value).toBe('110');
    expect((inputs[3] as HTMLInputElement).value).toBe('32');
  });

  it('splits YearStart and YearEnd payload and keeps duplicated ticker values isolated', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();

    render(
      <MissingPriceModal
        isOpen={true}
        onClose={vi.fn()}
        year={2025}
        missingPrices={[
          buildMissingPrice('AAPL', { priceType: 'YearStart', date: '2024-12-31' }),
          buildMissingPrice('AAPL', { priceType: 'YearEnd', date: '2025-12-31' }),
        ]}
        onSubmit={onSubmit}
      />,
    );

    const inputs = screen.getAllByRole('spinbutton');
    expect(inputs).toHaveLength(4);

    await user.type(inputs[0]!, '90');
    await user.type(inputs[1]!, '31');
    await user.type(inputs[2]!, '110');
    await user.type(inputs[3]!, '32');

    await user.click(screen.getByRole('button', { name: '計算績效' }));

    expect(onSubmit).toHaveBeenCalledTimes(1);
    expect(onSubmit).toHaveBeenCalledWith({
      yearStartPrices: {
        AAPL: { price: 90, exchangeRate: 31 },
      },
      yearEndPrices: {
        AAPL: { price: 110, exchangeRate: 32 },
      },
    });
  });
});
