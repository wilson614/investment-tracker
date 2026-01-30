import { useQuery } from '@tanstack/react-query';
import { assetsApi } from '../api/assetsApi';

export const ASSETS_KEYS = {
  all: ['assets'] as const,
  summary: () => [...ASSETS_KEYS.all, 'summary'] as const,
};

export function useTotalAssets() {
  const query = useQuery({
    queryKey: ASSETS_KEYS.summary(),
    queryFn: () => assetsApi.getSummary(),
  });

  return {
    summary: query.data,
    isLoading: query.isLoading,
    error: query.error,
    refetch: query.refetch,
  };
}
