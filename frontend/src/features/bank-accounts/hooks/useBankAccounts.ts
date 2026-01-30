import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { bankAccountsApi } from '../api/bankAccountsApi';
import type { CreateBankAccountRequest, UpdateBankAccountRequest } from '../types';
import { useToast } from '../../../components/common';

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
      toast.success('銀行帳戶建立成功');
    },
    onError: (err: Error) => {
      toast.error(err.message || '建立失敗');
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateBankAccountRequest }) =>
      bankAccountsApi.update(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: BANK_ACCOUNTS_QUERY_KEY });
      toast.success('銀行帳戶更新成功');
    },
    onError: (err: Error) => {
      toast.error(err.message || '更新失敗');
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => bankAccountsApi.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: BANK_ACCOUNTS_QUERY_KEY });
      toast.success('銀行帳戶已刪除');
    },
    onError: (err: Error) => {
      toast.error(err.message || '刪除失敗');
    },
  });

  return {
    bankAccounts: query.data || [],
    isLoading: query.isLoading,
    error: query.error ? (query.error instanceof Error ? query.error.message : 'Unknown error') : null,
    refetch: query.refetch,
    createBankAccount: createMutation.mutateAsync,
    updateBankAccount: (id: string, data: UpdateBankAccountRequest) => updateMutation.mutateAsync({ id, data }),
    deleteBankAccount: deleteMutation.mutateAsync,
    isCreating: createMutation.isPending,
    isUpdating: updateMutation.isPending,
    isDeleting: deleteMutation.isPending,
  };
}
