import { fetchApi } from '../../../services/api';
import type {
  FixedDepositResponse,
  CreateFixedDepositRequest,
  UpdateFixedDepositRequest,
  CloseFixedDepositRequest,
} from '../types';

export const fixedDepositsApi = {
  /** 取得定存清單 */
  getFixedDeposits: () => fetchApi<FixedDepositResponse[]>('/fixed-deposits'),

  /** 依 ID 取得定存 */
  getFixedDeposit: (id: string) =>
    fetchApi<FixedDepositResponse>(`/fixed-deposits/${id}`),

  /** 建立定存 */
  createFixedDeposit: (data: CreateFixedDepositRequest) =>
    fetchApi<FixedDepositResponse>('/fixed-deposits', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  /** 更新定存 */
  updateFixedDeposit: (id: string, data: UpdateFixedDepositRequest) =>
    fetchApi<FixedDepositResponse>(`/fixed-deposits/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data),
    }),

  /** 結清定存（到期/提前解約） */
  closeFixedDeposit: (id: string, data: CloseFixedDepositRequest) =>
    fetchApi<FixedDepositResponse>(`/fixed-deposits/${id}/close`, {
      method: 'POST',
      body: JSON.stringify(data),
    }),
};

export const {
  getFixedDeposits,
  getFixedDeposit,
  createFixedDeposit,
  updateFixedDeposit,
  closeFixedDeposit,
} = fixedDepositsApi;
