import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { CreditCardsPage } from '../pages/CreditCardsPage';
import type { CreditCardResponse, InstallmentResponse, UpcomingPaymentMonth } from '../features/credit-cards/types';

vi.mock('../features/credit-cards/hooks/useCreditCards', () => ({
  useCreditCards: vi.fn(),
}));

vi.mock('../features/credit-cards/hooks/useInstallments', () => ({
  useInstallments: vi.fn(),
}));

vi.mock('../features/credit-cards/components/CreditCardForm', () => ({
  CreditCardForm: () => null,
}));

vi.mock('../features/credit-cards/components/InstallmentForm', () => ({
  InstallmentForm: () => null,
}));

import { useCreditCards } from '../features/credit-cards/hooks/useCreditCards';
import { useInstallments } from '../features/credit-cards/hooks/useInstallments';

const mockedUseCreditCards = vi.mocked(useCreditCards);
const mockedUseInstallments = vi.mocked(useInstallments);

type CreditCardsHookResult = ReturnType<typeof useCreditCards>;
type InstallmentsHookResult = ReturnType<typeof useInstallments>;

const noopCreditCardsRefetch: CreditCardsHookResult['refetch'] = vi.fn();
const noopCreateCreditCard: CreditCardsHookResult['createCreditCard'] = vi.fn();
const noopUpdateCreditCard: CreditCardsHookResult['updateCreditCard'] = vi.fn();

const noopInstallmentsRefetch: InstallmentsHookResult['refetch'] = vi.fn();
const noopInstallmentsRefetchUpcoming: InstallmentsHookResult['refetchUpcoming'] = vi.fn();
const noopCreateInstallment: InstallmentsHookResult['createInstallment'] = vi.fn();
const noopDeleteInstallment: InstallmentsHookResult['deleteInstallment'] = vi.fn();

const baseCard: CreditCardResponse = {
  id: 'card-1',
  bankName: '測試銀行',
  cardName: '測試卡',
  paymentDueDay: 10,
  note: null,
  activeInstallmentsCount: 1,
  totalUnpaidBalance: 12000,
  createdAt: '2026-01-01T00:00:00.000Z',
  updatedAt: '2026-01-01T00:00:00.000Z',
};

const secondCard: CreditCardResponse = {
  id: 'card-2',
  bankName: '第二銀行',
  cardName: '第二卡',
  paymentDueDay: 15,
  note: null,
  activeInstallmentsCount: 0,
  totalUnpaidBalance: 0,
  createdAt: '2026-01-01T00:00:00.000Z',
  updatedAt: '2026-01-01T00:00:00.000Z',
};

const baseInstallment: InstallmentResponse = {
  id: 'inst-1',
  creditCardId: 'card-1',
  creditCardName: '測試卡',
  description: '筆電',
  totalAmount: 36000,
  numberOfInstallments: 12,
  remainingInstallments: 8,
  monthlyPayment: 3000,
  firstPaymentDate: '2026-01-01',
  status: 'Active',
  note: null,
  unpaidBalance: 24000,
  paidAmount: 12000,
  progressPercentage: 33.33,
  createdAt: '2026-01-01T00:00:00.000Z',
  updatedAt: '2026-01-01T00:00:00.000Z',
};

const baseUpcoming: UpcomingPaymentMonth[] = [
  {
    month: '2026-02',
    totalAmount: 3000,
    payments: [
      {
        installmentId: 'inst-1',
        description: '筆電',
        creditCardName: '測試卡',
        amount: 3000,
      },
    ],
  },
];

function setupCreditCards(overrides?: Partial<ReturnType<typeof useCreditCards>>) {
  mockedUseCreditCards.mockReturnValue({
    creditCards: [],
    isLoading: false,
    error: null,
    refetch: noopCreditCardsRefetch,
    createCreditCard: noopCreateCreditCard,
    updateCreditCard: noopUpdateCreditCard,
    isCreating: false,
    isUpdating: false,
    ...overrides,
  });
}

function setupInstallments(overrides?: Partial<ReturnType<typeof useInstallments>>) {
  mockedUseInstallments.mockReturnValue({
    installments: [],
    upcomingPayments: [],
    isLoading: false,
    isFetching: false,
    isPlaceholderData: false,
    isUpcomingLoading: false,
    error: null,
    upcomingError: null,
    refetch: noopInstallmentsRefetch,
    refetchUpcoming: noopInstallmentsRefetchUpcoming,
    createInstallment: noopCreateInstallment,
    deleteInstallment: noopDeleteInstallment,
    isCreating: false,
    isDeleting: false,
    ...overrides,
  });
}

describe('CreditCardsPage loading behavior', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('初始載入時保留頁首並使用區塊 skeleton，而非整頁 skeleton gate', () => {
    setupCreditCards({
      creditCards: [],
      isLoading: true,
      error: null,
    });

    setupInstallments({
      installments: [],
      upcomingPayments: [],
      isLoading: false,
      isUpcomingLoading: false,
      error: null,
      upcomingError: null,
    });

    render(<CreditCardsPage />);

    expect(screen.getByRole('heading', { name: '信用卡' })).toBeInTheDocument();
    expect(screen.getByLabelText('信用卡清單載入中')).toBeInTheDocument();
    expect(screen.getByLabelText('分期區塊載入中')).toBeInTheDocument();
    expect(screen.getByLabelText('未來付款載入中')).toBeInTheDocument();
    expect(screen.queryByLabelText('信用卡頁面載入中')).not.toBeInTheDocument();
  });

  it('有信用卡資料時即使 loading 中也先掛載分期區塊，避免 selectedCard gate 晚掛載', () => {
    setupCreditCards({
      creditCards: [baseCard],
      isLoading: true,
      error: null,
    });

    setupInstallments({
      installments: [baseInstallment],
      upcomingPayments: baseUpcoming,
      isLoading: false,
      isUpcomingLoading: false,
      error: null,
      upcomingError: null,
    });

    render(<CreditCardsPage />);

    expect(screen.getByRole('heading', { name: '測試卡 分期清單' })).toBeInTheDocument();
    expect(screen.getByText(/管理該卡分期與未繳餘額/)).toBeInTheDocument();
    expect(screen.getAllByText('筆電').length).toBeGreaterThan(0);
  });

  it('upcoming 區塊在 background loading 時保留既有內容，不退回載入文案', () => {
    setupCreditCards({
      creditCards: [baseCard],
      isLoading: false,
      error: null,
    });

    setupInstallments({
      installments: [baseInstallment],
      upcomingPayments: baseUpcoming,
      isLoading: false,
      isUpcomingLoading: true,
      error: null,
      upcomingError: null,
    });

    render(<CreditCardsPage />);

    expect(screen.getByText('未來三個月付款預覽')).toBeInTheDocument();
    expect(screen.getByText('2026 年 02 月')).toBeInTheDocument();
    expect(screen.queryByText('載入未來付款中...')).not.toBeInTheDocument();
  });

  it('refresh 背景 refetch 且使用 placeholderData 時，保留既有分期內容與數字欄位', () => {
    setupCreditCards({
      creditCards: [baseCard, secondCard],
      isLoading: false,
      error: null,
    });

    setupInstallments({
      installments: [baseInstallment],
      upcomingPayments: baseUpcoming,
      isLoading: false,
      isFetching: true,
      isPlaceholderData: true,
      isUpcomingLoading: false,
      error: null,
      upcomingError: null,
    });

    render(<CreditCardsPage />);

    expect(screen.getByRole('heading', { name: '測試卡 分期清單' })).toBeInTheDocument();
    expect(screen.getAllByText('筆電').length).toBeGreaterThan(0);
    expect(screen.queryByLabelText('分期清單載入中')).not.toBeInTheDocument();

    const activeInstallmentSlots = screen.getAllByTestId('credit-card-active-installments-slot');
    const totalUnpaidSlots = screen.getAllByTestId('credit-card-total-unpaid-slot');

    expect(activeInstallmentSlots[0]).toHaveAttribute('data-slot-loading', 'false');
    expect(activeInstallmentSlots[1]).toHaveAttribute('data-slot-loading', 'false');
    expect(totalUnpaidSlots[0]).toHaveAttribute('data-slot-loading', 'false');
    expect(totalUnpaidSlots[1]).toHaveAttribute('data-slot-loading', 'false');

    expect(screen.getByTestId('installment-total-amount-slot')).toHaveAttribute('data-slot-loading', 'false');
    expect(screen.getByTestId('installment-monthly-payment-slot')).toHaveAttribute('data-slot-loading', 'false');
  });

  it('切換信用卡且分期查詢使用 placeholderData 時，顯示 skeleton 並避免空狀態閃動', () => {
    setupCreditCards({
      creditCards: [baseCard, secondCard],
      isLoading: false,
      error: null,
    });

    mockedUseInstallments.mockImplementation((options) => {
      const baseResult: InstallmentsHookResult = {
        installments: [],
        upcomingPayments: baseUpcoming,
        isLoading: false,
        isFetching: false,
        isPlaceholderData: false,
        isUpcomingLoading: false,
        error: null,
        upcomingError: null,
        refetch: noopInstallmentsRefetch,
        refetchUpcoming: noopInstallmentsRefetchUpcoming,
        createInstallment: noopCreateInstallment,
        deleteInstallment: noopDeleteInstallment,
        isCreating: false,
        isDeleting: false,
      };

      if (options?.creditCardId === baseCard.id) {
        return {
          ...baseResult,
          installments: [baseInstallment],
        };
      }

      return {
        ...baseResult,
        installments: [baseInstallment],
        isFetching: true,
        isPlaceholderData: true,
      };
    });

    render(<CreditCardsPage />);

    expect(screen.getAllByText('筆電').length).toBeGreaterThan(0);

    fireEvent.click(screen.getByText('第二卡'));

    expect(screen.getByRole('heading', { name: '第二卡 分期清單' })).toBeInTheDocument();
    expect(screen.getByLabelText('分期清單載入中')).toBeInTheDocument();
    expect(screen.queryByText('尚無分期紀錄')).not.toBeInTheDocument();
  });

  it('切卡過渡期間僅 selected card 數字欄位顯示 placeholder，避免全部卡片跳動', () => {
    setupCreditCards({
      creditCards: [baseCard, secondCard],
      isLoading: false,
      error: null,
    });

    mockedUseInstallments.mockImplementation((options) => {
      const baseResult: InstallmentsHookResult = {
        installments: [],
        upcomingPayments: baseUpcoming,
        isLoading: false,
        isFetching: false,
        isPlaceholderData: false,
        isUpcomingLoading: false,
        error: null,
        upcomingError: null,
        refetch: noopInstallmentsRefetch,
        refetchUpcoming: noopInstallmentsRefetchUpcoming,
        createInstallment: noopCreateInstallment,
        deleteInstallment: noopDeleteInstallment,
        isCreating: false,
        isDeleting: false,
      };

      if (options?.creditCardId === baseCard.id) {
        return {
          ...baseResult,
          installments: [baseInstallment],
        };
      }

      return {
        ...baseResult,
        installments: [baseInstallment],
        isFetching: true,
        isPlaceholderData: true,
      };
    });

    render(<CreditCardsPage />);

    fireEvent.click(screen.getByText('第二卡'));

    const activeInstallmentSlots = screen.getAllByTestId('credit-card-active-installments-slot');
    const totalUnpaidSlots = screen.getAllByTestId('credit-card-total-unpaid-slot');

    expect(activeInstallmentSlots[0]).toHaveAttribute('data-slot-loading', 'false');
    expect(activeInstallmentSlots[0].querySelector('.animate-pulse')).toBeNull();
    expect(activeInstallmentSlots[1]).toHaveAttribute('data-slot-loading', 'true');
    expect(activeInstallmentSlots[1].querySelector('.animate-pulse')).not.toBeNull();

    expect(totalUnpaidSlots[0]).toHaveAttribute('data-slot-loading', 'false');
    expect(totalUnpaidSlots[1]).toHaveAttribute('data-slot-loading', 'true');
  });

  it('切卡過渡期間 upcoming 區塊不因分期 placeholder 過渡而顯示 value skeleton', () => {
    setupCreditCards({
      creditCards: [baseCard, secondCard],
      isLoading: false,
      error: null,
    });

    mockedUseInstallments.mockImplementation((options) => {
      const baseResult: InstallmentsHookResult = {
        installments: [],
        upcomingPayments: baseUpcoming,
        isLoading: false,
        isFetching: false,
        isPlaceholderData: false,
        isUpcomingLoading: false,
        error: null,
        upcomingError: null,
        refetch: noopInstallmentsRefetch,
        refetchUpcoming: noopInstallmentsRefetchUpcoming,
        createInstallment: noopCreateInstallment,
        deleteInstallment: noopDeleteInstallment,
        isCreating: false,
        isDeleting: false,
      };

      if (options?.creditCardId === baseCard.id) {
        return {
          ...baseResult,
          installments: [baseInstallment],
        };
      }

      return {
        ...baseResult,
        installments: [baseInstallment],
        isFetching: true,
        isPlaceholderData: true,
      };
    });

    render(<CreditCardsPage />);

    fireEvent.click(screen.getByText('第二卡'));

    const monthTotalSlot = screen.getByTestId('upcoming-month-total-slot');
    const paymentAmountSlot = screen.getByTestId('upcoming-payment-amount-slot');

    expect(monthTotalSlot).toHaveAttribute('data-slot-loading', 'false');
    expect(monthTotalSlot.querySelector('.animate-pulse')).toBeNull();
    expect(paymentAmountSlot).toHaveAttribute('data-slot-loading', 'false');
    expect(paymentAmountSlot.querySelector('.animate-pulse')).toBeNull();
  });

  it('數字欄位容器具有寬度保留 class，避免 layout shift', () => {
    setupCreditCards({
      creditCards: [baseCard],
      isLoading: false,
      error: null,
    });

    setupInstallments({
      installments: [baseInstallment],
      upcomingPayments: baseUpcoming,
      isLoading: false,
      isFetching: false,
      isPlaceholderData: false,
      isUpcomingLoading: false,
      error: null,
      upcomingError: null,
    });

    render(<CreditCardsPage />);

    expect(screen.getByTestId('credit-card-total-unpaid-slot').className).toContain('min-w-[12ch]');
    expect(screen.getByTestId('installment-total-amount-slot').className).toContain('min-w-[10ch]');
    expect(screen.getByTestId('installment-progress-percentage-slot').className).toContain('min-w-[6ch]');
    expect(screen.getByTestId('upcoming-payment-amount-slot').className).toContain('min-w-[9ch]');
  });
});
