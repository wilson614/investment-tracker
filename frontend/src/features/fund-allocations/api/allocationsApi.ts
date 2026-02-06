import { fetchApi } from '../../../services/api';
import type {
  AllocationSummary,
  CreateFundAllocationRequest,
  FundAllocation,
  UpdateFundAllocationRequest,
} from '../types';

export const allocationsApi = {
  /** 取得資產配置摘要 */
  getAllocations: () => fetchApi<AllocationSummary>('/fundallocations'),

  /** 建立資產配置 */
  createAllocation: (request: CreateFundAllocationRequest) =>
    fetchApi<FundAllocation>('/fundallocations', {
      method: 'POST',
      body: JSON.stringify(request),
    }),

  /** 更新資產配置 */
  updateAllocation: (id: string, request: UpdateFundAllocationRequest) =>
    fetchApi<void>(`/fundallocations/${id}`, {
      method: 'PUT',
      body: JSON.stringify(request),
    }),

  /** 刪除資產配置 */
  deleteAllocation: (id: string) =>
    fetchApi<void>(`/fundallocations/${id}`, { method: 'DELETE' }),
};
