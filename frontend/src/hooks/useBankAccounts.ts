import { useState, useEffect, useCallback } from 'react';
import { bankAccountsApi } from '../api/bankAccountsApi';
import type { BankAccount, CreateBankAccountRequest, UpdateBankAccountRequest } from '../types';
import { useToast } from '../components/common';

export function useBankAccounts() {
  const [bankAccounts, setBankAccounts] = useState<BankAccount[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const toast = useToast();

  const fetchBankAccounts = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const data = await bankAccountsApi.getAll();
      setBankAccounts(data);
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Failed to fetch bank accounts';
      setError(msg);
      // Don't show toast on initial load error, just set error state
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchBankAccounts();
  }, [fetchBankAccounts]);

  const createBankAccount = async (data: CreateBankAccountRequest) => {
    try {
      await bankAccountsApi.create(data);
      toast.success('銀行帳戶建立成功');
      fetchBankAccounts();
      return true;
    } catch (err) {
      toast.error(err instanceof Error ? err.message : '建立失敗');
      return false;
    }
  };

  const updateBankAccount = async (id: string, data: UpdateBankAccountRequest) => {
    try {
      await bankAccountsApi.update(id, data);
      toast.success('銀行帳戶更新成功');
      fetchBankAccounts();
      return true;
    } catch (err) {
      toast.error(err instanceof Error ? err.message : '更新失敗');
      return false;
    }
  };

  const deleteBankAccount = async (id: string) => {
    if (!confirm('確定要刪除此銀行帳戶嗎？此動作無法復原。')) return;

    try {
      await bankAccountsApi.delete(id);
      toast.success('銀行帳戶已刪除');
      fetchBankAccounts();
      return true;
    } catch (err) {
      toast.error(err instanceof Error ? err.message : '刪除失敗');
      return false;
    }
  };

  return {
    bankAccounts,
    isLoading,
    error,
    refetch: fetchBankAccounts,
    createBankAccount,
    updateBankAccount,
    deleteBankAccount
  };
}
