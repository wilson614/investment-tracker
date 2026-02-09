import { fetchApi } from '../../../services/api';
import type {
  InstallmentResponse,
  CreateInstallmentRequest,
  UpdateInstallmentRequest,
  UpcomingPaymentMonth,
} from '../types';

type InstallmentStatus = InstallmentResponse['status'];

interface UpcomingPaymentsApiResponse {
  months: Array<{
    month: string;
    totalPayment: number;
    installments: Array<{
      id: string;
      description: string;
      creditCardName: string;
      monthlyPayment: number;
    }>;
  }>;
}

function buildStatusQuery(status?: InstallmentStatus): string {
  return status ? `?status=${encodeURIComponent(status)}` : '';
}

function formatMonth(dateString: string): string {
  if (!dateString) {
    return '';
  }

  const date = new Date(dateString);
  if (Number.isNaN(date.getTime())) {
    return dateString;
  }

  const year = date.getUTCFullYear();
  const month = (date.getUTCMonth() + 1).toString().padStart(2, '0');
  return `${year}-${month}`;
}

function mapUpcomingMonths(response: UpcomingPaymentsApiResponse): UpcomingPaymentMonth[] {
  return response.months.map((item) => ({
    month: formatMonth(item.month),
    totalAmount: item.totalPayment,
    payments: item.installments.map((payment) => ({
      installmentId: payment.id,
      description: payment.description,
      creditCardName: payment.creditCardName,
      amount: payment.monthlyPayment,
    })),
  }));
}

export const installmentsApi = {
  /** 取得指定信用卡的分期清單 */
  getInstallments: (creditCardId: string, status?: InstallmentStatus) =>
    fetchApi<InstallmentResponse[]>(`/credit-cards/${creditCardId}/installments${buildStatusQuery(status)}`),

  /** 取得使用者所有信用卡的分期清單 */
  getAllInstallments: (status?: InstallmentStatus) =>
    fetchApi<InstallmentResponse[]>(`/installments${buildStatusQuery(status)}`),

  /** 建立分期 */
  createInstallment: (data: CreateInstallmentRequest) => {
    const { creditCardId, ...payload } = data;
    const bodyPayload = { ...payload, creditCardId };
    return fetchApi<InstallmentResponse>(`/credit-cards/${creditCardId}/installments`, {
      method: 'POST',
      body: JSON.stringify(bodyPayload),
    });
  },

  /** 更新分期 */
  updateInstallment: (id: string, data: UpdateInstallmentRequest) =>
    fetchApi<InstallmentResponse>(`/installments/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data),
    }),

  /** 取得未來 N 個月分期付款預覽 */
  getUpcomingPayments: async (months = 3) => {
    const response = await fetchApi<UpcomingPaymentsApiResponse>(
      `/installments/upcoming?months=${encodeURIComponent(months.toString())}`
    );
    return mapUpcomingMonths(response);
  },
};
