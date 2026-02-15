import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { bankAccountsApi } from '../api/bankAccountsApi';
import type { CloseBankAccountRequest, CreateBankAccountRequest, UpdateBankAccountRequest } from '../types';
import { useToast } from '../../../components/common';
import { getErrorMessage } from '../../../utils/errorMapping';
import { ASSETS_KEYS } from '../../total-assets/hooks/useTotalAssets';
import { invalidateAssetsSummaryQuery } from '../../../utils/cacheInvalidation';

export const BANK_ACCOUNTS_QUERY_KEY = ['bankAccounts'];

export function useBankAccounts() {
  const queryClient = useQueryClient();
  const toast = useToast();

  const query = useQuery({
    queryKey: BANK_ACCOUNTS_QUERY_KEY,
    queryFn: () => bankAccountsApi.getAll(),
  });

  const createMutation = useMutation({
    mutationFn: (data: CreateBankAccountRequest) => bankAccountsApi.create(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: BANK_ACCOUNTS_QUERY_KEY });
      invalidateAssetsSummaryQuery(queryClient, ASSETS_KEYS.summary());
      toast.success('銀行帳戶建立成功');
    },
    onError: (err: Error) => {
      toast.error(getErrorMessage(err.message || '建立失敗'));
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateBankAccountRequest }) =>
      bankAccountsApi.update(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: BANK_ACCOUNTS_QUERY_KEY });
      invalidateAssetsSummaryQuery(queryClient, ASSETS_KEYS.summary());
      toast.success('銀行帳戶更新成功');
    },
    onError: (err: Error) => {
      toast.error(getErrorMessage(err.message || '更新失敗'));
    },
  });

  const closeMutation = useMutation({
    mutationFn: ({ id, data }: { id: string; data?: CloseBankAccountRequest }) =>
      bankAccountsApi.closeBankAccount(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: BANK_ACCOUNTS_QUERY_KEY });
      invalidateAssetsSummaryQuery(queryClient, ASSETS_KEYS.summary());
      toast.success('定存帳戶已結清');
    },
    onError: (err: Error) => {
      toast.error(getErrorMessage(err.message || '結清失敗'));
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => bankAccountsApi.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: BANK_ACCOUNTS_QUERY_KEY });
      invalidateAssetsSummaryQuery(queryClient, ASSETS_KEYS.summary());
      toast.success('銀行帳戶已刪除');
    },
    onError: (err: Error) => {
      toast.error(getErrorMessage(err.message || '刪除失敗'));
    },
  });

  return {
    bankAccounts: query.data || [],
    isLoading: query.isLoading,
    error: query.error ? (query.error instanceof Error ? query.error.message : 'Unknown error') : null,
    refetch: query.refetch,
    createBankAccount: createMutation.mutateAsync,
    updateBankAccount: (id: string, data: UpdateBankAccountRequest) => updateMutation.mutateAsync({ id, data }),
    closeBankAccount: (id: string, data?: CloseBankAccountRequest) => closeMutation.mutateAsync({ id, data }),
    deleteBankAccount: deleteMutation.mutateAsync,
    isCreating: createMutation.isPending,
    isUpdating: updateMutation.isPending,
    isClosing: closeMutation.isPending,
    isDeleting: deleteMutation.isPending,
  };
}
