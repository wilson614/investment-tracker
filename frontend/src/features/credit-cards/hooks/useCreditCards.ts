import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useToast } from '../../../components/common';
import { getErrorMessage } from '../../../utils/errorMapping';
import { creditCardsApi } from '../api/creditCardsApi';
import type { CreateCreditCardRequest, UpdateCreditCardRequest } from '../types';

export const CREDIT_CARDS_QUERY_KEY = ['creditCards'];

export function useCreditCards() {
  const queryClient = useQueryClient();
  const toast = useToast();

  const query = useQuery({
    queryKey: CREDIT_CARDS_QUERY_KEY,
    queryFn: () => creditCardsApi.getCreditCards(),
  });

  const createMutation = useMutation({
    mutationFn: (data: CreateCreditCardRequest) => creditCardsApi.createCreditCard(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CREDIT_CARDS_QUERY_KEY });
      toast.success('信用卡建立成功');
    },
    onError: (err: Error) => {
      toast.error(getErrorMessage(err.message || '建立失敗'));
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateCreditCardRequest }) =>
      creditCardsApi.updateCreditCard(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CREDIT_CARDS_QUERY_KEY });
      toast.success('信用卡更新成功');
    },
    onError: (err: Error) => {
      toast.error(getErrorMessage(err.message || '更新失敗'));
    },
  });

  const deactivateMutation = useMutation({
    mutationFn: (id: string) => creditCardsApi.deactivateCreditCard(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CREDIT_CARDS_QUERY_KEY });
      toast.success('信用卡已停用');
    },
    onError: (err: Error) => {
      toast.error(getErrorMessage(err.message || '停用失敗'));
    },
  });

  return {
    creditCards: query.data || [],
    isLoading: query.isLoading,
    error: query.error ? (query.error instanceof Error ? query.error.message : 'Unknown error') : null,
    refetch: query.refetch,
    createCreditCard: createMutation.mutateAsync,
    updateCreditCard: (id: string, data: UpdateCreditCardRequest) => updateMutation.mutateAsync({ id, data }),
    deactivateCreditCard: deactivateMutation.mutateAsync,
    isCreating: createMutation.isPending,
    isUpdating: updateMutation.isPending,
    isDeactivating: deactivateMutation.isPending,
  };
}
