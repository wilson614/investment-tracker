import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useToast } from '../../../components/common';
import { getErrorMessage } from '../../../utils/errorMapping';
import { allocationsApi } from '../api/allocationsApi';
import { ASSETS_KEYS } from '../../total-assets/hooks/useTotalAssets';
import { invalidateAssetsSummaryQuery } from '../../../utils/cacheInvalidation';
import type { CreateFundAllocationRequest, UpdateFundAllocationRequest } from '../types';

export const FUND_ALLOCATIONS_QUERY_KEY = ['fundAllocations'];

const invalidateAllocationRelatedQueries = (queryClient: ReturnType<typeof useQueryClient>) => {
  queryClient.invalidateQueries({ queryKey: FUND_ALLOCATIONS_QUERY_KEY });
  invalidateAssetsSummaryQuery(queryClient, ASSETS_KEYS.summary());
};

export function useFundAllocations() {
  const queryClient = useQueryClient();
  const toast = useToast();

  const query = useQuery({
    queryKey: FUND_ALLOCATIONS_QUERY_KEY,
    queryFn: () => allocationsApi.getAllocations(),
  });

  const createMutation = useMutation({
    mutationFn: (data: CreateFundAllocationRequest) => allocationsApi.createAllocation(data),
    onSuccess: () => {
      invalidateAllocationRelatedQueries(queryClient);
      toast.success('資金配置建立成功');
    },
    onError: (err: Error) => {
      toast.error(getErrorMessage(err.message || '建立失敗'));
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateFundAllocationRequest }) =>
      allocationsApi.updateAllocation(id, data),
    onSuccess: () => {
      invalidateAllocationRelatedQueries(queryClient);
      toast.success('資金配置更新成功');
    },
    onError: (err: Error) => {
      toast.error(getErrorMessage(err.message || '更新失敗'));
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => allocationsApi.deleteAllocation(id),
    onSuccess: () => {
      invalidateAllocationRelatedQueries(queryClient);
      toast.success('資金配置已刪除');
    },
    onError: (err: Error) => {
      toast.error(getErrorMessage(err.message || '刪除失敗'));
    },
  });

  return {
    allocations: query.data?.allocations ?? [],
    totalAllocated: query.data?.totalAllocated ?? 0,
    unallocated: query.data?.unallocated ?? 0,
    isLoading: query.isLoading,
    error: query.error ? (query.error instanceof Error ? getErrorMessage(query.error.message) : 'Unknown error') : null,
    refetch: query.refetch,
    createAllocation: createMutation.mutateAsync,
    updateAllocation: (id: string, data: UpdateFundAllocationRequest) => updateMutation.mutateAsync({ id, data }),
    deleteAllocation: deleteMutation.mutateAsync,
    isCreating: createMutation.isPending,
    isUpdating: updateMutation.isPending,
    isDeleting: deleteMutation.isPending,
  };
}
