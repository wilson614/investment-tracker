import { fetchApi } from '../../../services/api';
import type { TotalAssetsSummary } from '../types';

export const assetsApi = {
  /** Get total assets summary including investment and bank accounts */
  getSummary: () => fetchApi<TotalAssetsSummary>('/assets/summary'),
};
