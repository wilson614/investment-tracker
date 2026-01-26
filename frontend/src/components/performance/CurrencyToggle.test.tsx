import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CurrencyToggle } from './CurrencyToggle';

const STORAGE_KEY = 'performance_currency_mode';

describe('CurrencyToggle', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders source/home labels with currencies', () => {
    render(
      <CurrencyToggle
        value="source"
        onChange={() => {}}
        sourceCurrency="USD"
        homeCurrency="TWD"
      />
    );

    expect(screen.getByRole('button', { name: '原幣 (USD)' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '本位幣 (TWD)' })).toBeInTheDocument();
  });

  it('clicking home saves to localStorage and triggers onChange', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(
      <CurrencyToggle
        value="source"
        onChange={onChange}
        sourceCurrency="USD"
        homeCurrency="TWD"
      />
    );

    await user.click(screen.getByRole('button', { name: '本位幣 (TWD)' }));

    expect(localStorage.getItem(STORAGE_KEY)).toBe('home');
    expect(onChange).toHaveBeenCalledWith('home');
  });

  it('reads stored value on mount and triggers onChange when different', () => {
    localStorage.setItem(STORAGE_KEY, 'home');

    const onChange = vi.fn();

    render(
      <CurrencyToggle
        value="source"
        onChange={onChange}
        sourceCurrency="USD"
        homeCurrency="TWD"
      />
    );

    expect(onChange).toHaveBeenCalledWith('home');
  });

  it('does not call onChange on mount when stored value matches', () => {
    localStorage.setItem(STORAGE_KEY, 'home');

    const onChange = vi.fn();

    render(
      <CurrencyToggle
        value="home"
        onChange={onChange}
        sourceCurrency="USD"
        homeCurrency="TWD"
      />
    );

    expect(onChange).not.toHaveBeenCalled();
  });

  it('respects disabled state (no localStorage write, no onChange)', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(
      <CurrencyToggle
        value="source"
        onChange={onChange}
        sourceCurrency="USD"
        homeCurrency="TWD"
        disabled
      />
    );

    await user.click(screen.getByRole('button', { name: '本位幣 (TWD)' }));

    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
    expect(onChange).not.toHaveBeenCalled();
  });
});
