import { fetchApi } from '../../../services/api';
import type { AvailableFundsSummaryResponse } from '../types/availableFunds';

export const availableFundsApi = {
  getAvailableFundsSummary: (): Promise<AvailableFundsSummaryResponse> =>
    fetchApi<AvailableFundsSummaryResponse>('/available-funds'),
};
