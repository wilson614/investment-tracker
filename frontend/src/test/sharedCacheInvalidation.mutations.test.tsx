import { describe, it, expect, beforeEach, vi } from 'vitest';
import type { ReactNode } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useCreditCards } from '../features/credit-cards/hooks/useCreditCards';
import { useInstallments } from '../features/credit-cards/hooks/useInstallments';
import { useBankAccounts } from '../features/bank-accounts/hooks/useBankAccounts';
import { useFundAllocations } from '../features/fund-allocations/hooks/useFundAllocations';
import type { CreateCreditCardRequest, CreateInstallmentRequest } from '../features/credit-cards/types';
import type { CreateBankAccountRequest } from '../features/bank-accounts/types';
import type { CreateFundAllocationRequest } from '../features/fund-allocations/types';

vi.mock('../features/credit-cards/api/creditCardsApi', () => ({
  creditCardsApi: {
    getCreditCards: vi.fn(),
    createCreditCard: vi.fn(),
    updateCreditCard: vi.fn(),
  },
}));

vi.mock('../features/credit-cards/api/installmentsApi', () => ({
  installmentsApi: {
    getInstallments: vi.fn(),
    getAllInstallments: vi.fn(),
    getUpcomingPayments: vi.fn(),
    createInstallment: vi.fn(),
    deleteInstallment: vi.fn(),
  },
}));

vi.mock('../features/bank-accounts/api/bankAccountsApi', () => ({
  bankAccountsApi: {
    getAll: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    closeBankAccount: vi.fn(),
    delete: vi.fn(),
  },
}));

vi.mock('../features/fund-allocations/api/allocationsApi', () => ({
  allocationsApi: {
    getAllocations: vi.fn(),
    createAllocation: vi.fn(),
    updateAllocation: vi.fn(),
    deleteAllocation: vi.fn(),
  },
}));

vi.mock('../components/common', () => ({
  useToast: () => ({
    success: vi.fn(),
    error: vi.fn(),
    warning: vi.fn(),
    info: vi.fn(),
  }),
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

import { creditCardsApi } from '../features/credit-cards/api/creditCardsApi';
import { installmentsApi } from '../features/credit-cards/api/installmentsApi';
import { bankAccountsApi } from '../features/bank-accounts/api/bankAccountsApi';
import { allocationsApi } from '../features/fund-allocations/api/allocationsApi';
import {
  invalidateAssetsSummaryQuery,
  invalidatePerformanceAndAssetsCaches,
  invalidatePerformanceLocalStorageCache,
} from '../utils/cacheInvalidation';

const mockedCreditCardsApi = vi.mocked(creditCardsApi, { deep: true });
const mockedInstallmentsApi = vi.mocked(installmentsApi, { deep: true });
const mockedBankAccountsApi = vi.mocked(bankAccountsApi, { deep: true });
const mockedAllocationsApi = vi.mocked(allocationsApi, { deep: true });
const mockedInvalidateAssetsSummaryQuery = vi.mocked(invalidateAssetsSummaryQuery);
const mockedInvalidatePerformanceLocalStorageCache = vi.mocked(invalidatePerformanceLocalStorageCache);
const mockedInvalidatePerformanceAndAssetsCaches = vi.mocked(invalidatePerformanceAndAssetsCaches);

describe('shared cache invalidation on mutation success', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();

    mockedCreditCardsApi.getCreditCards.mockResolvedValue([]);
    mockedCreditCardsApi.createCreditCard.mockResolvedValue({
      id: 'card-1',
      bankName: 'Test Bank',
      cardName: 'My Card',
      paymentDueDay: 10,
      note: null,
      activeInstallmentsCount: 0,
      totalUnpaidBalance: 0,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    });

    mockedInstallmentsApi.getAllInstallments.mockResolvedValue([]);
    mockedInstallmentsApi.getUpcomingPayments.mockResolvedValue([]);
    mockedInstallmentsApi.createInstallment.mockResolvedValue({
      id: 'inst-1',
      creditCardId: 'card-1',
      creditCardName: 'My Card',
      description: 'Laptop',
      totalAmount: 30000,
      numberOfInstallments: 12,
      remainingInstallments: 12,
      monthlyPayment: 2500,
      firstPaymentDate: '2026-01-01',
      status: 'Active',
      note: null,
      unpaidBalance: 30000,
      paidAmount: 0,
      progressPercentage: 0,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    });

    mockedBankAccountsApi.getAll.mockResolvedValue([]);
    mockedBankAccountsApi.create.mockResolvedValue({
      id: 'bank-1',
      userId: 'user-1',
      bankName: 'Bank',
      accountType: 'Savings',
      totalAssets: 1000,
      interestRate: 1.2,
      monthlyInterest: 1,
      yearlyInterest: 12,
      isActive: true,
      currency: 'TWD',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    });
    mockedBankAccountsApi.closeBankAccount.mockResolvedValue({
      id: 'bank-1',
      userId: 'user-1',
      bankName: 'Bank',
      accountType: 'FixedDeposit',
      totalAssets: 0,
      interestRate: 1.2,
      monthlyInterest: 0,
      yearlyInterest: 0,
      isActive: false,
      currency: 'TWD',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    });

    mockedAllocationsApi.getAllocations.mockResolvedValue({
      totalAllocated: 0,
      unallocated: 10000,
      allocations: [],
    });
    mockedAllocationsApi.createAllocation.mockResolvedValue({
      id: 'allocation-1',
      purpose: 'Savings',
      purposeDisplayName: '儲蓄',
      amount: 5000,
      isDisposable: true,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    });
  });

  function createWrapper(queryClient: QueryClient) {
    return ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
  }

  it('useCreditCards create mutation triggers assets-only invalidation', async () => {
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });

    const invalidateQueriesSpy = vi.spyOn(queryClient, 'invalidateQueries').mockResolvedValue();

    const { result } = renderHook(() => useCreditCards(), {
      wrapper: createWrapper(queryClient),
    });

    const payload: CreateCreditCardRequest = {
      bankName: 'Test Bank',
      cardName: 'My Card',
      paymentDueDay: 10,
      note: null,
    };

    await result.current.createCreditCard(payload);

    await waitFor(() => {
      expect(invalidateQueriesSpy).toHaveBeenCalledWith({ queryKey: ['creditCards'] });
      expect(mockedInvalidateAssetsSummaryQuery).toHaveBeenCalledTimes(1);
      expect(mockedInvalidatePerformanceLocalStorageCache).not.toHaveBeenCalled();
      expect(mockedInvalidatePerformanceAndAssetsCaches).not.toHaveBeenCalled();
    });

    const [passedQueryClient, passedAssetsKey] = mockedInvalidateAssetsSummaryQuery.mock.calls[0] as [QueryClient, readonly string[]];
    expect(passedQueryClient).toBe(queryClient);
    expect(passedAssetsKey).toEqual(['assets', 'summary']);
  });

  it('useBankAccounts create mutation triggers assets-only invalidation', async () => {
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });

    const invalidateQueriesSpy = vi.spyOn(queryClient, 'invalidateQueries').mockResolvedValue();

    const { result } = renderHook(() => useBankAccounts(), {
      wrapper: createWrapper(queryClient),
    });

    const payload: CreateBankAccountRequest = {
      bankName: 'Bank',
      accountType: 'Savings',
      totalAssets: 1000,
      interestRate: 1.2,
      currency: 'TWD',
    };

    await result.current.createBankAccount(payload);

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

  it('useBankAccounts close mutation triggers assets-only invalidation', async () => {
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });

    const invalidateQueriesSpy = vi.spyOn(queryClient, 'invalidateQueries').mockResolvedValue();

    const { result } = renderHook(() => useBankAccounts(), {
      wrapper: createWrapper(queryClient),
    });

    await result.current.closeBankAccount('bank-1');

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

  it('useInstallments create mutation triggers assets-only invalidation', async () => {
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });

    const invalidateQueriesSpy = vi.spyOn(queryClient, 'invalidateQueries').mockResolvedValue();

    const { result } = renderHook(() => useInstallments(), {
      wrapper: createWrapper(queryClient),
    });

    const payload: CreateInstallmentRequest = {
      creditCardId: 'card-1',
      description: 'Laptop',
      totalAmount: 30000,
      numberOfInstallments: 12,
      firstPaymentDate: '2026-01-01',
      note: null,
    };

    await result.current.createInstallment(payload);

    await waitFor(() => {
      expect(invalidateQueriesSpy).toHaveBeenCalledWith({ queryKey: ['installments'] });
      expect(invalidateQueriesSpy).toHaveBeenCalledWith({ queryKey: ['installmentsUpcoming'] });
      expect(invalidateQueriesSpy).toHaveBeenCalledWith({ queryKey: ['creditCards'] });
      expect(mockedInvalidateAssetsSummaryQuery).toHaveBeenCalledTimes(1);
      expect(mockedInvalidatePerformanceLocalStorageCache).not.toHaveBeenCalled();
      expect(mockedInvalidatePerformanceAndAssetsCaches).not.toHaveBeenCalled();
    });

    const [passedQueryClient, passedAssetsKey] = mockedInvalidateAssetsSummaryQuery.mock.calls[0] as [QueryClient, readonly string[]];
    expect(passedQueryClient).toBe(queryClient);
    expect(passedAssetsKey).toEqual(['assets', 'summary']);
  });

  it('useFundAllocations create mutation triggers assets-only invalidation', async () => {
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });

    const invalidateQueriesSpy = vi.spyOn(queryClient, 'invalidateQueries').mockResolvedValue();

    const { result } = renderHook(() => useFundAllocations(), {
      wrapper: createWrapper(queryClient),
    });

    const payload: CreateFundAllocationRequest = {
      purpose: 'Savings',
      amount: 5000,
      isDisposable: true,
    };

    await result.current.createAllocation(payload);

    await waitFor(() => {
      expect(invalidateQueriesSpy).toHaveBeenCalledWith({ queryKey: ['fundAllocations'] });
      expect(mockedInvalidateAssetsSummaryQuery).toHaveBeenCalledTimes(1);
      expect(mockedInvalidatePerformanceLocalStorageCache).not.toHaveBeenCalled();
      expect(mockedInvalidatePerformanceAndAssetsCaches).not.toHaveBeenCalled();
    });

    const [passedQueryClient, passedAssetsKey] = mockedInvalidateAssetsSummaryQuery.mock.calls[0] as [QueryClient, readonly string[]];
    expect(passedQueryClient).toBe(queryClient);
    expect(passedAssetsKey).toEqual(['assets', 'summary']);
  });
});
