import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useToast } from '../../../components/common';
import { getErrorMessage } from '../../../utils/errorMapping';
import { ASSETS_KEYS } from '../../total-assets/hooks/useTotalAssets';
import { fixedDepositsApi } from '../api/fixedDepositsApi';
import type {
  CloseFixedDepositRequest,
  CreateFixedDepositRequest,
  UpdateFixedDepositRequest,
} from '../types';

export const FIXED_DEPOSITS_QUERY_KEY = ['fixedDeposits'];

export function useFixedDeposits() {
  const queryClient = useQueryClient();
  const toast = useToast();

  const query = useQuery({
    queryKey: FIXED_DEPOSITS_QUERY_KEY,
    queryFn: () => fixedDepositsApi.getFixedDeposits(),
  });

  const createMutation = useMutation({
    mutationFn: (data: CreateFixedDepositRequest) => fixedDepositsApi.createFixedDeposit(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: FIXED_DEPOSITS_QUERY_KEY });
      queryClient.invalidateQueries({ queryKey: ASSETS_KEYS.summary() });
      toast.success('定存建立成功');
    },
    onError: (err: Error) => {
      toast.error(getErrorMessage(err.message || '建立失敗'));
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateFixedDepositRequest }) =>
      fixedDepositsApi.updateFixedDeposit(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: FIXED_DEPOSITS_QUERY_KEY });
      queryClient.invalidateQueries({ queryKey: ASSETS_KEYS.summary() });
      toast.success('定存更新成功');
    },
    onError: (err: Error) => {
      toast.error(getErrorMessage(err.message || '更新失敗'));
    },
  });

  const closeMutation = useMutation({
    mutationFn: ({ id, data }: { id: string; data: CloseFixedDepositRequest }) =>
      fixedDepositsApi.closeFixedDeposit(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: FIXED_DEPOSITS_QUERY_KEY });
      queryClient.invalidateQueries({ queryKey: ASSETS_KEYS.summary() });
      toast.success('定存已結清');
    },
    onError: (err: Error) => {
      toast.error(getErrorMessage(err.message || '結清失敗'));
    },
  });

  return {
    fixedDeposits: query.data || [],
    isLoading: query.isLoading,
    error: query.error ? (query.error instanceof Error ? query.error.message : 'Unknown error') : null,
    refetch: query.refetch,
    createFixedDeposit: createMutation.mutateAsync,
    updateFixedDeposit: (id: string, data: UpdateFixedDepositRequest) =>
      updateMutation.mutateAsync({ id, data }),
    closeFixedDeposit: (id: string, data: CloseFixedDepositRequest) =>
      closeMutation.mutateAsync({ id, data }),
    isCreating: createMutation.isPending,
    isUpdating: updateMutation.isPending,
    isClosing: closeMutation.isPending,
  };
}
