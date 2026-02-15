import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BankAccountImportModal } from '../components/import/BankAccountImportModal';
import { fetchApi } from '../services/api';
import {
  invalidateAssetsSummaryQuery,
  invalidatePerformanceAndAssetsCaches,
  invalidatePerformanceLocalStorageCache,
} from '../utils/cacheInvalidation';

const toastMocks = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
  warning: vi.fn(),
  info: vi.fn(),
}));

vi.mock('../services/api', () => ({
  fetchApi: vi.fn(),
}));

vi.mock('../components/common', () => ({
  useToast: () => toastMocks,
}));

vi.mock('../utils/cacheInvalidation', async () => {
  const actual = await vi.importActual<typeof import('../utils/cacheInvalidation')>(
    '../utils/cacheInvalidation'
  );

  return {
    ...actual,
    invalidateAssetsSummaryQuery: vi.fn(),
    invalidatePerformanceLocalStorageCache: vi.fn(),
    invalidatePerformanceAndAssetsCaches: vi.fn(),
  };
});

const mockedFetchApi = vi.mocked(fetchApi);
const mockedInvalidateAssetsSummaryQuery = vi.mocked(invalidateAssetsSummaryQuery);
const mockedInvalidatePerformanceLocalStorageCache = vi.mocked(invalidatePerformanceLocalStorageCache);
const mockedInvalidatePerformanceAndAssetsCaches = vi.mocked(invalidatePerformanceAndAssetsCaches);

describe('BankAccountImportModal cache invalidation', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
  });

  it('execute import triggers assets invalidate only and skips performance invalidation helpers', async () => {
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });

    const invalidateQueriesSpy = vi.spyOn(queryClient, 'invalidateQueries').mockResolvedValue();

    mockedFetchApi
      .mockResolvedValueOnce({
        items: [{
          bankName: 'Bank',
          action: 'create',
          changeDetails: ['新增帳戶'],
        }],
        validationErrors: [],
      })
      .mockResolvedValueOnce({
        createdCount: 1,
        updatedCount: 0,
        skippedCount: 0,
      });

    const onClose = vi.fn();
    const csvFile = new File(
      ['bankName,accountType,totalAssets,interestRate,currency\nBank,Savings,1000,1.2,TWD'],
      'bank-accounts.csv',
      { type: 'text/csv' }
    );

    render(
      <QueryClientProvider client={queryClient}>
        <BankAccountImportModal
          isOpen={true}
          onClose={onClose}
          file={csvFile}
        />
      </QueryClientProvider>
    );

    const user = userEvent.setup();

    await screen.findByText(/已解析/);

    await user.click(screen.getByRole('button', { name: '預覽' }));

    await waitFor(() => {
      expect(mockedFetchApi).toHaveBeenCalledWith('/bank-accounts/import', expect.objectContaining({
        method: 'POST',
      }));
    });

    await user.click(screen.getByRole('button', { name: '確認匯入' }));

    await waitFor(() => {
      expect(invalidateQueriesSpy).toHaveBeenCalledWith({ queryKey: ['bankAccounts'] });
      expect(mockedInvalidateAssetsSummaryQuery).toHaveBeenCalledTimes(1);
      expect(mockedInvalidatePerformanceLocalStorageCache).not.toHaveBeenCalled();
      expect(mockedInvalidatePerformanceAndAssetsCaches).not.toHaveBeenCalled();
    });

    const [passedQueryClient, passedAssetsKey] = mockedInvalidateAssetsSummaryQuery.mock.calls[0] as [QueryClient, readonly string[]];
    expect(passedQueryClient).toBe(queryClient);
    expect(passedAssetsKey).toEqual(['assets', 'summary']);
  });
});
