import { fetchApi } from '../services/api';
import type {
  BankAccount,
  CreateBankAccountRequest,
  UpdateBankAccountRequest
} from '../types';

export const bankAccountsApi = {
  /** Get all bank accounts */
  getAll: () => fetchApi<BankAccount[]>('/bank-accounts'),

  /** Get bank account by ID */
  getById: (id: string) => fetchApi<BankAccount>(`/bank-accounts/${id}`),

  /** Create new bank account */
  create: (data: CreateBankAccountRequest) =>
    fetchApi<BankAccount>('/bank-accounts', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  /** Update bank account */
  update: (id: string, data: UpdateBankAccountRequest) =>
    fetchApi<BankAccount>(`/bank-accounts/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data),
    }),

  /** Delete bank account */
  delete: (id: string) =>
    fetchApi<void>(`/bank-accounts/${id}`, { method: 'DELETE' }),
};
