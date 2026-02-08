import { useQuery } from '@tanstack/react-query';
import { availableFundsApi } from '../api/availableFundsApi';

export const AVAILABLE_FUNDS_QUERY_KEY = ['availableFundsSummary'] as const;

export function useAvailableFunds() {
  const query = useQuery({
    queryKey: AVAILABLE_FUNDS_QUERY_KEY,
    queryFn: () => availableFundsApi.getAvailableFundsSummary(),
    staleTime: 5 * 60 * 1000,
    placeholderData: (previousData) => previousData,
    refetchOnWindowFocus: false,
  });

  return {
    summary: query.data,
    isLoading: query.isLoading,
    error: query.error ? (query.error instanceof Error ? query.error.message : 'Unknown error') : null,
    refetch: query.refetch,
  };
}
