import { describe, it, expect, beforeEach, vi } from 'vitest';
import type { ReactNode } from 'react';
import { act, renderHook } from '@testing-library/react';
import { AuthProvider, useAuth } from '../hooks/useAuth';
import { authApi } from '../services/api';

vi.mock('../services/api', () => ({
  authApi: {
    login: vi.fn(),
    register: vi.fn(),
    logout: vi.fn(),
  },
}));

const mockedAuthApi = vi.mocked(authApi, { deep: true });

function Wrapper({ children }: { children: ReactNode }) {
  return <AuthProvider>{children}</AuthProvider>;
}

function seedPerformanceStorage() {
  localStorage.setItem('perf_years_portfolio-a', JSON.stringify({ years: [2025] }));
  localStorage.setItem('perf_data_portfolio-a_2025', JSON.stringify({ year: 2025 }));
  localStorage.setItem('perf_years_v2_aggregate', JSON.stringify({ years: [2024, 2025] }));
  localStorage.setItem('perf_data_v2_aggregate_2025', JSON.stringify({ year: 2025 }));
}

describe('useAuth logout performance cache cleanup', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.clearAllMocks();
    mockedAuthApi.logout.mockResolvedValue(undefined);
  });

  it('clears perf_years_/perf_data_ localStorage keys on successful logout flow', async () => {
    localStorage.setItem('refreshToken', 'refresh-token-1');
    seedPerformanceStorage();

    const { result } = renderHook(() => useAuth(), {
      wrapper: Wrapper,
    });

    await act(async () => {
      await result.current.logout();
    });

    expect(mockedAuthApi.logout).toHaveBeenCalledWith('refresh-token-1');

    expect(localStorage.getItem('perf_years_portfolio-a')).toBeNull();
    expect(localStorage.getItem('perf_data_portfolio-a_2025')).toBeNull();
    expect(localStorage.getItem('perf_years_v2_aggregate')).toBeNull();
    expect(localStorage.getItem('perf_data_v2_aggregate_2025')).toBeNull();
  });

  it('still clears perf_years_/perf_data_ localStorage keys when authApi.logout fails', async () => {
    localStorage.setItem('refreshToken', 'refresh-token-2');
    seedPerformanceStorage();
    mockedAuthApi.logout.mockRejectedValueOnce(new Error('logout failed'));

    const { result } = renderHook(() => useAuth(), {
      wrapper: Wrapper,
    });

    await act(async () => {
      await result.current.logout();
    });

    expect(mockedAuthApi.logout).toHaveBeenCalledWith('refresh-token-2');

    expect(localStorage.getItem('perf_years_portfolio-a')).toBeNull();
    expect(localStorage.getItem('perf_data_portfolio-a_2025')).toBeNull();
    expect(localStorage.getItem('perf_years_v2_aggregate')).toBeNull();
    expect(localStorage.getItem('perf_data_v2_aggregate_2025')).toBeNull();
  });
});
