import { useQuery } from '@tanstack/react-query';
import { assetsApi } from '../api/assetsApi';

export const ASSETS_SUMMARY_QUERY_KEY = ['assets', 'summary'] as const;

export const ASSETS_KEYS = {
  all: ['assets'] as const,
  summary: () => ASSETS_SUMMARY_QUERY_KEY,
  summaryQuery: ASSETS_SUMMARY_QUERY_KEY,
};

export function useTotalAssets() {
  const query = useQuery({
    queryKey: ASSETS_KEYS.summaryQuery,
    queryFn: () => assetsApi.getSummary(),
    staleTime: 5 * 60 * 1000,
    placeholderData: (previousData) => previousData,
    refetchOnWindowFocus: false,
  });

  return {
    summary: query.data,
    isLoading: query.isLoading,
    error: query.error,
    refetch: query.refetch,
  };
}
