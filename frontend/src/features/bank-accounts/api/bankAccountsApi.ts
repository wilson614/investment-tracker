import { fetchApi } from '../../../services/api';
import type {
  BankAccount,
  CloseBankAccountRequest,
  CreateBankAccountRequest,
  UpdateBankAccountRequest
} from '../types';

export const bankAccountsApi = {
  /** 取得所有銀行帳戶 */
  getAll: () => fetchApi<BankAccount[]>('/bank-accounts'),

  /** 依 ID 取得銀行帳戶 */
  getById: (id: string) => fetchApi<BankAccount>(`/bank-accounts/${id}`),

  /** 建立新銀行帳戶 */
  create: (data: CreateBankAccountRequest) =>
    fetchApi<BankAccount>('/bank-accounts', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  /** 更新銀行帳戶 */
  update: (id: string, data: UpdateBankAccountRequest) =>
    fetchApi<BankAccount>(`/bank-accounts/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data),
    }),

  /** 結清定存帳戶 */
  closeBankAccount: (id: string, data: CloseBankAccountRequest = {}) =>
    fetchApi<BankAccount>(`/bank-accounts/${id}/close`, {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  /** 刪除銀行帳戶 */
  delete: (id: string) =>
    fetchApi<void>(`/bank-accounts/${id}`, { method: 'DELETE' }),
};
