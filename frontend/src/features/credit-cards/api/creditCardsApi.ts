import { fetchApi } from '../../../services/api';
import type {
  CreditCardResponse,
  CreateCreditCardRequest,
  UpdateCreditCardRequest,
} from '../types';

export const creditCardsApi = {
  /** 取得信用卡清單 */
  getCreditCards: (includeInactive?: boolean) => {
    const query = includeInactive === undefined ? '' : `?includeInactive=${includeInactive}`;
    return fetchApi<CreditCardResponse[]>(`/credit-cards${query}`);
  },

  /** 依 ID 取得信用卡 */
  getCreditCard: (id: string) =>
    fetchApi<CreditCardResponse>(`/credit-cards/${id}`),

  /** 建立信用卡 */
  createCreditCard: (data: CreateCreditCardRequest) =>
    fetchApi<CreditCardResponse>('/credit-cards', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  /** 更新信用卡 */
  updateCreditCard: (id: string, data: UpdateCreditCardRequest) =>
    fetchApi<CreditCardResponse>(`/credit-cards/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data),
    }),

  /** 停用信用卡 */
  deactivateCreditCard: (id: string) =>
    fetchApi<void>(`/credit-cards/${id}`, { method: 'DELETE' }),
};
