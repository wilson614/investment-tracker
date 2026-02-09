import { fetchApi } from '../../../services/api';
import type {
  CreditCardResponse,
  CreateCreditCardRequest,
  UpdateCreditCardRequest,
} from '../types';

export const creditCardsApi = {
  /** 取得信用卡清單 */
  getCreditCards: () => fetchApi<CreditCardResponse[]>('/credit-cards'),

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

};
