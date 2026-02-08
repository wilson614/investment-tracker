import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useToast } from '../../../components/common';
import { getErrorMessage } from '../../../utils/errorMapping';
import { ASSETS_KEYS } from '../../total-assets/hooks/useTotalAssets';
import { installmentsApi } from '../api/installmentsApi';
import type {
  CreateInstallmentRequest,
  InstallmentResponse,
  UpdateInstallmentRequest,
} from '../types';

export const INSTALLMENTS_QUERY_KEY = ['installments'];
export const INSTALLMENTS_UPCOMING_QUERY_KEY = ['installmentsUpcoming'];

const installmentsKeyByCard = (creditCardId: string) => [...INSTALLMENTS_QUERY_KEY, 'card', creditCardId] as const;
const installmentsKeyByStatus = (status?: InstallmentResponse['status']) =>
  [...INSTALLMENTS_QUERY_KEY, 'all', status ?? 'ALL'] as const;
const upcomingPaymentsKey = (months: number) => [...INSTALLMENTS_UPCOMING_QUERY_KEY, months] as const;

export function useInstallments(options?: {
  creditCardId?: string;
  status?: InstallmentResponse['status'];
  upcomingMonths?: number;
}) {
  const queryClient = useQueryClient();
  const toast = useToast();

  const creditCardId = options?.creditCardId;
  const status = options?.status;
  const upcomingMonths = options?.upcomingMonths ?? 3;

  const installmentsQuery = useQuery({
    queryKey: creditCardId ? installmentsKeyByCard(creditCardId) : installmentsKeyByStatus(status),
    queryFn: () =>
      creditCardId
        ? installmentsApi.getInstallments(creditCardId, status)
        : installmentsApi.getAllInstallments(status),
  });

  const upcomingQuery = useQuery({
    queryKey: upcomingPaymentsKey(upcomingMonths),
    queryFn: () => installmentsApi.getUpcomingPayments(upcomingMonths),
  });

  const invalidateInstallmentQueries = () => {
    queryClient.invalidateQueries({ queryKey: INSTALLMENTS_QUERY_KEY });
    queryClient.invalidateQueries({ queryKey: INSTALLMENTS_UPCOMING_QUERY_KEY });
    queryClient.invalidateQueries({ queryKey: ['creditCards'] });
    queryClient.invalidateQueries({ queryKey: ASSETS_KEYS.summary() });
  };

  const createMutation = useMutation({
    mutationFn: (data: CreateInstallmentRequest) => installmentsApi.createInstallment(data),
    onSuccess: () => {
      invalidateInstallmentQueries();
      toast.success('分期建立成功');
    },
    onError: (err: Error) => {
      toast.error(getErrorMessage(err.message || '建立失敗'));
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateInstallmentRequest }) =>
      installmentsApi.updateInstallment(id, data),
    onSuccess: () => {
      invalidateInstallmentQueries();
      toast.success('分期更新成功');
    },
    onError: (err: Error) => {
      toast.error(getErrorMessage(err.message || '更新失敗'));
    },
  });

  const recordPaymentMutation = useMutation({
    mutationFn: (id: string) => installmentsApi.recordPayment(id),
    onSuccess: () => {
      invalidateInstallmentQueries();
      toast.success('已記錄月付');
    },
    onError: (err: Error) => {
      toast.error(getErrorMessage(err.message || '記錄付款失敗'));
    },
  });

  const payoffMutation = useMutation({
    mutationFn: (id: string) => installmentsApi.payoffInstallment(id),
    onSuccess: () => {
      invalidateInstallmentQueries();
      toast.success('分期已提前清償');
    },
    onError: (err: Error) => {
      toast.error(getErrorMessage(err.message || '提前清償失敗'));
    },
  });

  return {
    installments: installmentsQuery.data ?? [],
    upcomingPayments: upcomingQuery.data ?? [],
    isLoading: installmentsQuery.isLoading,
    isUpcomingLoading: upcomingQuery.isLoading,
    error: installmentsQuery.error
      ? installmentsQuery.error instanceof Error
        ? installmentsQuery.error.message
        : 'Unknown error'
      : null,
    upcomingError: upcomingQuery.error
      ? upcomingQuery.error instanceof Error
        ? upcomingQuery.error.message
        : 'Unknown error'
      : null,
    refetch: installmentsQuery.refetch,
    refetchUpcoming: upcomingQuery.refetch,
    createInstallment: createMutation.mutateAsync,
    updateInstallment: (id: string, data: UpdateInstallmentRequest) =>
      updateMutation.mutateAsync({ id, data }),
    recordPayment: recordPaymentMutation.mutateAsync,
    payoffInstallment: payoffMutation.mutateAsync,
    isCreating: createMutation.isPending,
    isUpdating: updateMutation.isPending,
    isRecordingPayment: recordPaymentMutation.isPending,
    isPayingOff: payoffMutation.isPending,
  };
}
