import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import userEvent from '@testing-library/user-event';
import { StockImportButton } from '../components/import/StockImportButton';
import { formatDateISO } from '../utils/csvParser';
import { transactionApi } from '../services/api';
import type {
  StockImportExecuteRequest,
  StockImportExecuteResponse,
  StockImportExecuteStatusResponse,
  StockImportPreviewRequest,
  StockImportPreviewResponse,
  StockImportPreviewRow,
  StockImportSelectedFormat,
} from '../types';

vi.mock('../services/api', () => ({
  transactionApi: {
    create: vi.fn(),
    previewImport: vi.fn(),
    executeImport: vi.fn(),
    getImportStatus: vi.fn(),
  },
}));

const mockedTransactionApi = vi.mocked(transactionApi, { deep: true });
const TEST_PORTFOLIO_ID = 'portfolio-balance-action';

function buildImportCsvFile(rows: string[] = []): File {
  const headers = '日期,代碼,買賣,股數,單價,手續費,市場,幣別';
  const sampleRows = rows.length > 0
    ? rows
    : ['"2026/01/22","2330","買進","1000","625","10","TW","TWD"'];

  const content = `${headers}\n${sampleRows.join('\n')}`;
  const file = new File([content], 'stock-import.csv', { type: 'text/csv' });

  Object.defineProperty(file, 'text', {
    configurable: true,
    value: () => Promise.resolve(content),
  });

  return file;
}

function buildPreviewRow(overrides: Partial<StockImportPreviewRow> = {}): StockImportPreviewRow {
  return {
    rowNumber: 1,
    tradeDate: '2026-01-22',
    rawSecurityName: 'ROW-DEFAULT',
    ticker: '2330',
    tradeSide: 'buy',
    confirmedTradeSide: 'buy',
    quantity: 1000,
    unitPrice: 625,
    fees: 10,
    taxes: 0,
    netSettlement: -625010,
    currency: 'TWD',
    status: 'valid',
    usesPartialHistoryAssumption: false,
    actionsRequired: [],
    ...overrides,
  };
}

function buildPreviewResponse(params: {
  sessionId?: string;
  detectedFormat?: StockImportPreviewResponse['detectedFormat'];
  selectedFormat?: StockImportSelectedFormat;
  rows?: StockImportPreviewRow[];
} = {}): StockImportPreviewResponse {
  const rows = params.rows ?? [buildPreviewRow()];

  return {
    sessionId: params.sessionId ?? 'session-preview',
    detectedFormat: params.detectedFormat ?? 'broker_statement',
    selectedFormat: params.selectedFormat ?? 'broker_statement',
    summary: {
      totalRows: rows.length,
      validRows: rows.filter((row) => row.status === 'valid').length,
      requiresActionRows: rows.filter((row) => row.status === 'requires_user_action').length,
      invalidRows: rows.filter((row) => row.status === 'invalid').length,
    },
    rows,
    errors: [],
  };
}

function buildExecuteResponse(overrides: Partial<StockImportExecuteResponse> = {}): StockImportExecuteResponse {
  return {
    status: 'committed',
    summary: {
      totalRows: 1,
      insertedRows: 1,
      failedRows: 0,
      errorCount: 0,
    },
    results: [
      {
        rowNumber: 1,
        success: true,
        transactionId: 'tx-1',
        message: 'Created',
      },
    ],
    errors: [],
    ...overrides,
  };
}

function buildExecuteStatusResponse(
  overrides: Partial<StockImportExecuteStatusResponse> = {},
): StockImportExecuteStatusResponse {
  return {
    sessionId: overrides.sessionId ?? 'session-status',
    portfolioId: overrides.portfolioId ?? TEST_PORTFOLIO_ID,
    executionStatus: overrides.executionStatus ?? 'completed',
    message: overrides.message ?? null,
    startedAtUtc: overrides.startedAtUtc ?? '2026-02-01T00:00:00Z',
    completedAtUtc: overrides.completedAtUtc ?? '2026-02-01T00:00:10Z',
    result: overrides.result ?? buildExecuteResponse(),
    ...overrides,
  };
}

async function openImportModalAndUploadCsv(user: ReturnType<typeof userEvent.setup>, file: File) {
  await user.click(screen.getByRole('button', { name: /匯入|import/i }));
  await screen.findByText('上傳 CSV 檔案');

  const fileInput = await waitFor(() => {
    const input = document.querySelector('input[type="file"]') as HTMLInputElement | null;
    expect(input).not.toBeNull();
    return input as HTMLInputElement;
  });

  fireEvent.change(fileInput, { target: { files: [file] } });

  await screen.findByRole('button', { name: /下一步|next/i });
}

async function moveToPreviewStep(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: /下一步|next/i }));

  await screen.findByRole('button', { name: /確認匯入|執行匯入|開始匯入/i });
}

async function requestPreview(user: ReturnType<typeof userEvent.setup>) {
  const previewButton = screen.queryByRole('button', {
    name: /預覽|重新預覽|產生預覽|preview/i,
  });

  if (previewButton) {
    const callCountBefore = mockedTransactionApi.previewImport.mock.calls.length;
    await user.click(previewButton);

    await waitFor(() => {
      expect(mockedTransactionApi.previewImport.mock.calls.length).toBeGreaterThan(callCountBefore);
    });

    return;
  }

  await waitFor(() => {
    expect(mockedTransactionApi.previewImport).toHaveBeenCalled();
  });
}

function getLatestPreviewRequest(): StockImportPreviewRequest {
  const latestCall = mockedTransactionApi.previewImport.mock.calls.at(-1);
  if (!latestCall) {
    throw new Error('previewImport 尚未被呼叫');
  }

  return latestCall[0] as StockImportPreviewRequest;
}

function getLatestExecuteRequest(): StockImportExecuteRequest {
  const latestCall = mockedTransactionApi.executeImport.mock.calls.at(-1);
  if (!latestCall) {
    throw new Error('executeImport 尚未被呼叫');
  }

  return latestCall[0] as StockImportExecuteRequest;
}


function findGlobalBalanceActionSelector(): HTMLSelectElement | null {
  const labeled = screen.queryByLabelText(/餘額不足預設處理方式/i);
  if (labeled instanceof HTMLSelectElement) {
    return labeled;
  }

  const byId = document.getElementById('global-balance-action-selector');
  if (byId instanceof HTMLSelectElement) {
    return byId;
  }

  return null;
}

function findGlobalTopUpTypeSelector(): HTMLSelectElement | null {
  const labeled = screen.queryByLabelText(/^補足交易類型$/i);
  if (labeled instanceof HTMLSelectElement) {
    return labeled;
  }

  const byId = document.getElementById('global-topup-transaction-type-selector');
  if (byId instanceof HTMLSelectElement) {
    return byId;
  }

  return null;
}


async function selectGlobalBalanceAction(
  user: ReturnType<typeof userEvent.setup>,
  action: 'Margin' | 'TopUp' | null,
) {
  const selector = await waitFor(() => {
    const control = findGlobalBalanceActionSelector();
    expect(control).not.toBeNull();
    return control as HTMLSelectElement;
  });

  const targetValue = action ?? '';
  const option = Array.from(selector.options).find((candidate) => candidate.value === targetValue);
  if (!option) {
    throw new Error(`找不到全域餘額處理方式選項: ${String(action)}`);
  }

  await user.selectOptions(selector, option.value);
}

async function selectGlobalTopUpTransactionType(
  user: ReturnType<typeof userEvent.setup>,
  transactionType: 'Deposit' | 'InitialBalance' | 'Interest' | 'OtherIncome',
) {
  const selector = await waitFor(() => {
    const control = findGlobalTopUpTypeSelector();
    expect(control).not.toBeNull();
    return control as HTMLSelectElement;
  });

  const option = Array.from(selector.options).find((candidate) => candidate.value === transactionType);
  if (!option) {
    throw new Error(`找不到全域補足交易類型選項: ${transactionType}`);
  }

  await user.selectOptions(selector, option.value);
}

function getRowContainer(rowMarker: string): HTMLElement {
  const markerNode = screen.getByText(rowMarker);

  return (
    markerNode.closest('tr')
    ?? markerNode.closest('[role="row"]')
    ?? markerNode.closest('li')
    ?? markerNode.parentElement
    ?? markerNode
  );
}


function findRowBalanceActionSelector(rowMarker: string): HTMLSelectElement {
  const rowContainer = getRowContainer(rowMarker);

  const selectors = within(rowContainer)
    .queryAllByRole('combobox')
    .filter((node): node is HTMLSelectElement => node instanceof HTMLSelectElement);

  const selector = selectors.find((select) => (
    Array.from(select.options).some((option) => option.value === 'Margin')
    || Array.from(select.options).some((option) => option.value === 'TopUp')
  ));

  if (!selector) {
    throw new Error(`找不到列 ${rowMarker} 的餘額處理方式下拉`);
  }

  return selector;
}

function findRowTopUpTypeSelector(rowMarker: string): HTMLSelectElement {
  const rowContainer = getRowContainer(rowMarker);

  const selectors = within(rowContainer)
    .queryAllByRole('combobox')
    .filter((node): node is HTMLSelectElement => node instanceof HTMLSelectElement);

  const selector = selectors.find((select) =>
    Array.from(select.options).some((option) => option.value === 'Deposit'),
  );

  if (!selector) {
    throw new Error(`找不到列 ${rowMarker} 的補足交易類型下拉`);
  }

  return selector;
}

function queryRowTopUpTypeSelector(rowMarker: string): HTMLSelectElement | null {
  const rowContainer = getRowContainer(rowMarker);

  const selectors = within(rowContainer)
    .queryAllByRole('combobox')
    .filter((node): node is HTMLSelectElement => node instanceof HTMLSelectElement);

  return selectors.find((select) =>
    Array.from(select.options).some((option) => option.value === 'Deposit'),
  ) ?? null;
}


async function selectRowBalanceActionSelection(
  user: ReturnType<typeof userEvent.setup>,
  rowMarker: string,
  value: 'default' | 'Margin' | 'TopUp',
) {
  const selector = findRowBalanceActionSelector(rowMarker);
  const option = Array.from(selector.options).find((candidate) => candidate.value === value);

  if (!option) {
    throw new Error(`找不到列 ${rowMarker} 的餘額處理方式選項: ${value}`);
  }

  await user.selectOptions(selector, option.value);
}

async function selectRowTopUpTransactionType(
  user: ReturnType<typeof userEvent.setup>,
  rowMarker: string,
  transactionType: 'Deposit' | 'InitialBalance' | 'Interest' | 'OtherIncome',
) {
  const selector = await waitFor(() => findRowTopUpTypeSelector(rowMarker));
  const option = Array.from(selector.options).find((candidate) => candidate.value === transactionType);

  if (!option) {
    throw new Error(`找不到列 ${rowMarker} 的補足交易類型選項: ${transactionType}`);
  }

  await user.selectOptions(selector, option.value);
}

async function addBaselineOpeningPosition(
  user: ReturnType<typeof userEvent.setup>,
  params: {
    baselineDate?: string;
    openingLedgerBalance?: string;
    ticker: string;
    quantity: string;
    totalCost: string;
  },
) {
  if (params.baselineDate) {
    fireEvent.change(screen.getByLabelText('目前持倉基準日'), {
      target: { value: params.baselineDate },
    });
  }

  if (params.openingLedgerBalance) {
    await user.type(screen.getByLabelText('目前用於投資的閒置資金（帳本餘額）'), params.openingLedgerBalance);
  }

  await user.click(screen.getByRole('button', { name: '新增持倉' }));

  await user.type(await screen.findByLabelText('目前持倉 1 ticker'), params.ticker);
  await user.type(screen.getByLabelText('目前持倉 1 quantity'), params.quantity);
  await user.type(screen.getByLabelText('目前持倉 1 total cost'), params.totalCost);
}

function expectNoSellBeforeBuyControls() {
  expect(screen.queryByText('賣先買後預設處理方式')).not.toBeInTheDocument();
  expect(screen.queryByLabelText('欄位說明：賣先買後預設處理方式')).not.toBeInTheDocument();
  expect(screen.queryByRole('columnheader', { name: '賣先買後處理' })).not.toBeInTheDocument();
}

async function executeImport(user: ReturnType<typeof userEvent.setup>) {
  const executeButton = await screen.findByRole('button', {
    name: /確認匯入|執行匯入|開始匯入/i,
  });

  await user.click(executeButton);
}

describe('Stock import balance action flow', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubGlobal('alert', vi.fn());
    localStorage.clear();

    mockedTransactionApi.previewImport.mockResolvedValue(buildPreviewResponse());
    mockedTransactionApi.executeImport.mockResolvedValue(buildExecuteResponse());
    mockedTransactionApi.getImportStatus.mockResolvedValue(buildExecuteStatusResponse());
  });

  afterEach(() => {
    localStorage.clear();
    vi.unstubAllGlobals();
  });

  it('sets global default balance action in legacy mode and sends execute payload with defaultBalanceAction', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-global-default',
        selectedFormat: 'legacy_csv',
        rows: [
          buildPreviewRow({
            rowNumber: 11,
            rawSecurityName: 'ROW-GLOBAL-DEFAULT',
            actionsRequired: ['select_balance_action'],
            status: 'requires_user_action',
            balanceDecision: {
              requiredAmount: 200,
              availableBalance: 100,
              shortfall: 100,
              action: null,
              topUpTransactionType: null,
              decisionScope: null,
            },
          }),
        ],
      }),
    );

    mockedTransactionApi.executeImport.mockResolvedValue(
      buildExecuteResponse({
        summary: {
          totalRows: 1,
          insertedRows: 1,
          failedRows: 0,
          errorCount: 0,
        },
      }),
    );

    render(
      <StockImportButton
        portfolioId={TEST_PORTFOLIO_ID}
        boundLedgerCurrencyCode="USD"
        onImportComplete={vi.fn()}
        onImportSuccess={vi.fn()}
      />,
    );

    const user = userEvent.setup();

    await openImportModalAndUploadCsv(user, buildImportCsvFile());
    await moveToPreviewStep(user);

    const formatSelector = await screen.findByLabelText(/匯入類型/i);
    expect(formatSelector).toBeInTheDocument();
    await user.selectOptions(formatSelector, 'legacy_csv');

    await requestPreview(user);

    const previewRequest = getLatestPreviewRequest();
    expect(previewRequest.portfolioId).toBe(TEST_PORTFOLIO_ID);
    expect(previewRequest.selectedFormat).toBe('legacy_csv');

    await selectGlobalBalanceAction(user, 'Margin');

    await executeImport(user);

    await waitFor(() => {
      expect(mockedTransactionApi.executeImport).toHaveBeenCalledTimes(1);
    });

    const executeRequest = getLatestExecuteRequest();
    expect(executeRequest.defaultBalanceAction).toEqual({
      action: 'Margin',
    });

    expect(executeRequest.rows).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          rowNumber: 11,
          balanceAction: 'Margin',
          confirmedTradeSide: 'buy',
          ticker: '2330',
        }),
      ]),
    );
  });

  it('includes baseline asOfDate/currentHoldings while preserving legacy baselineDate/openingPositions compatibility in broker mode and hides broker balance selectors', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-baseline-balance-compat',
        rows: [
          buildPreviewRow({
            rowNumber: 15,
            rawSecurityName: 'ROW-BASELINE-COMPAT',
            actionsRequired: ['select_balance_action'],
            status: 'requires_user_action',
            balanceDecision: {
              requiredAmount: 300,
              availableBalance: 200,
              shortfall: 100,
              action: null,
              topUpTransactionType: null,
              decisionScope: null,
            },
          }),
        ],
      }),
    );

    render(
      <StockImportButton
        portfolioId={TEST_PORTFOLIO_ID}
        boundLedgerCurrencyCode="USD"
        onImportComplete={vi.fn()}
        onImportSuccess={vi.fn()}
      />,
    );

    const user = userEvent.setup();
    const today = formatDateISO(new Date());

    await openImportModalAndUploadCsv(user, buildImportCsvFile());
    await moveToPreviewStep(user);

    expect(screen.queryByLabelText('目前持倉基準日')).not.toBeInTheDocument();

    await addBaselineOpeningPosition(user, {
      openingLedgerBalance: '5000',
      ticker: '2330',
      quantity: '10',
      totalCost: '6250',
    });

    await requestPreview(user);

    expect(findGlobalBalanceActionSelector()).toBeNull();
    expect(() => findRowBalanceActionSelector('ROW-BASELINE-COMPAT')).toThrow(
      '找不到列 ROW-BASELINE-COMPAT 的餘額處理方式下拉',
    );

    const previewRequest = getLatestPreviewRequest();
    expect(previewRequest.baseline).toEqual(
      expect.objectContaining({
        asOfDate: today,
        baselineDate: today,
        openingLedgerBalance: 5000,
      }),
    );
    expect(previewRequest.baseline?.openingCashBalance).toBeUndefined();

    const canonicalOpeningPosition = previewRequest.baseline?.currentHoldings?.[0];
    expect(canonicalOpeningPosition).toEqual(
      expect.objectContaining({
        ticker: '2330',
        quantity: 10,
        historicalTotalCost: 6250,
        totalCost: 6250,
      }),
    );

    const legacyOpeningPosition = previewRequest.baseline?.openingPositions?.[0];
    expect(legacyOpeningPosition).toEqual(
      expect.objectContaining({
        ticker: '2330',
        quantity: 10,
        historicalTotalCost: 6250,
        totalCost: 6250,
      }),
    );

    await executeImport(user);

    await waitFor(() => {
      expect(mockedTransactionApi.executeImport).toHaveBeenCalledTimes(1);
    });

    const executeRequest = getLatestExecuteRequest();
    expect(executeRequest.defaultBalanceAction).toEqual({
      action: 'TopUp',
      topUpTransactionType: 'Deposit',
    });
    expect(executeRequest.rows).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          rowNumber: 15,
          balanceAction: 'TopUp',
          topUpTransactionType: 'Deposit',
        }),
      ]),
    );
  });

  it('applies per-row override over global default in execute payload for legacy mode', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-row-override',
        selectedFormat: 'legacy_csv',
        rows: [
          buildPreviewRow({
            rowNumber: 21,
            rawSecurityName: 'ROW-OVERRIDE-1',
            actionsRequired: ['select_balance_action'],
            status: 'requires_user_action',
            balanceDecision: {
              requiredAmount: 300,
              availableBalance: 150,
              shortfall: 150,
              action: null,
              topUpTransactionType: null,
              decisionScope: null,
            },
          }),
          buildPreviewRow({
            rowNumber: 22,
            rawSecurityName: 'ROW-OVERRIDE-2',
            ticker: '2317',
            actionsRequired: ['select_balance_action'],
            status: 'requires_user_action',
            balanceDecision: {
              requiredAmount: 500,
              availableBalance: 400,
              shortfall: 100,
              action: null,
              topUpTransactionType: null,
              decisionScope: null,
            },
          }),
        ],
      }),
    );

    mockedTransactionApi.executeImport.mockResolvedValue(
      buildExecuteResponse({
        summary: {
          totalRows: 2,
          insertedRows: 2,
          failedRows: 0,
          errorCount: 0,
        },
      }),
    );

    render(
      <StockImportButton
        portfolioId={TEST_PORTFOLIO_ID}
        boundLedgerCurrencyCode="USD"
        onImportComplete={vi.fn()}
        onImportSuccess={vi.fn()}
      />,
    );

    const user = userEvent.setup();

    await openImportModalAndUploadCsv(user, buildImportCsvFile());
    await moveToPreviewStep(user);

    const formatSelector = await screen.findByLabelText(/匯入類型/i);
    expect(formatSelector).toBeInTheDocument();
    await user.selectOptions(formatSelector, 'legacy_csv');

    await requestPreview(user);

    await selectGlobalBalanceAction(user, 'Margin');
    await selectRowBalanceActionSelection(user, 'ROW-OVERRIDE-2', 'TopUp');
    await selectRowTopUpTransactionType(user, 'ROW-OVERRIDE-2', 'Deposit');

    await executeImport(user);

    await waitFor(() => {
      expect(mockedTransactionApi.executeImport).toHaveBeenCalledTimes(1);
    });

    const executeRequest = getLatestExecuteRequest();

    expect(executeRequest.defaultBalanceAction).toEqual({
      action: 'Margin',
    });

    const row1 = executeRequest.rows.find((row) => row.rowNumber === 21);
    const row2 = executeRequest.rows.find((row) => row.rowNumber === 22);

    expect(row1).toEqual(
      expect.objectContaining({
        rowNumber: 21,
        balanceAction: 'Margin',
      }),
    );

    expect(row2).toEqual(
      expect.objectContaining({
        rowNumber: 22,
        balanceAction: 'TopUp',
        topUpTransactionType: 'Deposit',
      }),
    );
  });

  it('hides topup type selector when bound ledger currency is TWD and broker mode hardcodes TopUp', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-twd-topup',
        rows: [
          buildPreviewRow({
            rowNumber: 23,
            rawSecurityName: 'ROW-TWD-TOPUP',
            actionsRequired: ['select_balance_action'],
            status: 'requires_user_action',
            balanceDecision: {
              requiredAmount: 500,
              availableBalance: 100,
              shortfall: 400,
              action: null,
              topUpTransactionType: null,
              decisionScope: null,
            },
          }),
        ],
      }),
    );

    mockedTransactionApi.executeImport.mockResolvedValue(buildExecuteResponse());

    render(
      <StockImportButton
        portfolioId={TEST_PORTFOLIO_ID}
        boundLedgerCurrencyCode="TWD"
        onImportComplete={vi.fn()}
        onImportSuccess={vi.fn()}
      />,
    );

    const user = userEvent.setup();

    await openImportModalAndUploadCsv(user, buildImportCsvFile());
    await moveToPreviewStep(user);
    await requestPreview(user);

    expect(findGlobalBalanceActionSelector()).toBeNull();
    expect(() => findRowBalanceActionSelector('ROW-TWD-TOPUP')).toThrow(
      '找不到列 ROW-TWD-TOPUP 的餘額處理方式下拉',
    );

    expect(findGlobalTopUpTypeSelector()).toBeNull();
    expect(queryRowTopUpTypeSelector('ROW-TWD-TOPUP')).toBeNull();

    const executeButton = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });
    expect(executeButton).toBeEnabled();

    await user.click(executeButton);

    await waitFor(() => {
      expect(mockedTransactionApi.executeImport).toHaveBeenCalledTimes(1);
    });

    const executeRequest = getLatestExecuteRequest();
    expect(executeRequest.defaultBalanceAction).toEqual({
      action: 'TopUp',
    });

    const row = executeRequest.rows.find((candidate) => candidate.rowNumber === 23);
    expect(row).toEqual(
      expect.objectContaining({
        rowNumber: 23,
        balanceAction: 'TopUp',
      }),
    );
    expect(row).not.toHaveProperty('topUpTransactionType');
  });

  it('TopUp type selector excludes ExchangeBuy and prevents forcing it via raw select event in legacy mode', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-topup-validation',
        selectedFormat: 'legacy_csv',
        rows: [
          buildPreviewRow({
            rowNumber: 31,
            rawSecurityName: 'ROW-TOPUP-NEEDS-TYPE',
            actionsRequired: ['select_balance_action'],
            status: 'requires_user_action',
            balanceDecision: {
              requiredAmount: 1000,
              availableBalance: 100,
              shortfall: 900,
              action: null,
              topUpTransactionType: null,
              decisionScope: null,
            },
          }),
        ],
      }),
    );

    mockedTransactionApi.executeImport.mockResolvedValue(buildExecuteResponse());

    render(
      <StockImportButton
        portfolioId={TEST_PORTFOLIO_ID}
        boundLedgerCurrencyCode="USD"
        onImportComplete={vi.fn()}
        onImportSuccess={vi.fn()}
      />,
    );

    const user = userEvent.setup();

    await openImportModalAndUploadCsv(user, buildImportCsvFile());
    await moveToPreviewStep(user);

    const formatSelector = await screen.findByLabelText(/匯入類型/i);
    expect(formatSelector).toBeInTheDocument();
    await user.selectOptions(formatSelector, 'legacy_csv');

    await requestPreview(user);

    await selectGlobalBalanceAction(user, 'TopUp');

    const executeButtonBeforeType = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });

    expect(executeButtonBeforeType).toBeDisabled();
    expect(screen.getByText('補足餘額需選擇交易類型')).toBeInTheDocument();

    const globalTopUpSelector = await waitFor(() => {
      const control = findGlobalTopUpTypeSelector();
      expect(control).not.toBeNull();
      return control as HTMLSelectElement;
    });

    expect(Array.from(globalTopUpSelector.options).some((option) => option.value === 'ExchangeBuy')).toBe(false);

    fireEvent.change(globalTopUpSelector, { target: { value: 'ExchangeBuy' } });

    const executeButtonAfterForcedExchangeBuy = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });
    expect(executeButtonAfterForcedExchangeBuy).toBeDisabled();

    await selectGlobalTopUpTransactionType(user, 'Deposit');

    const executeButtonAfterType = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });

    expect(executeButtonAfterType).toBeEnabled();

    await user.click(executeButtonAfterType);

    await waitFor(() => {
      expect(mockedTransactionApi.executeImport).toHaveBeenCalledTimes(1);
    });

    const executeRequest = getLatestExecuteRequest();
    expect(executeRequest.defaultBalanceAction).toEqual({
      action: 'TopUp',
      topUpTransactionType: 'Deposit',
    });

    expect(executeRequest.rows).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          rowNumber: 31,
          balanceAction: 'TopUp',
          topUpTransactionType: 'Deposit',
        }),
      ]),
    );
  });

  it('shows clear none-option semantics and blocks execute until every shortage row is decided in legacy mode', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-none-semantics',
        selectedFormat: 'legacy_csv',
        rows: [
          buildPreviewRow({
            rowNumber: 41,
            rawSecurityName: 'ROW-NONE-1',
            actionsRequired: ['select_balance_action'],
            status: 'requires_user_action',
            balanceDecision: {
              requiredAmount: 300,
              availableBalance: 150,
              shortfall: 150,
              action: null,
              topUpTransactionType: null,
              decisionScope: null,
            },
          }),
          buildPreviewRow({
            rowNumber: 42,
            rawSecurityName: 'ROW-NONE-2',
            ticker: '2603',
            actionsRequired: ['select_balance_action'],
            status: 'requires_user_action',
            balanceDecision: {
              requiredAmount: 200,
              availableBalance: 50,
              shortfall: 150,
              action: null,
              topUpTransactionType: null,
              decisionScope: null,
            },
          }),
        ],
      }),
    );

    render(
      <StockImportButton
        portfolioId={TEST_PORTFOLIO_ID}
        boundLedgerCurrencyCode="USD"
        onImportComplete={vi.fn()}
        onImportSuccess={vi.fn()}
      />,
    );

    const user = userEvent.setup();

    await openImportModalAndUploadCsv(user, buildImportCsvFile());
    await moveToPreviewStep(user);

    const formatSelector = await screen.findByLabelText(/匯入類型/i);
    expect(formatSelector).toBeInTheDocument();
    await user.selectOptions(formatSelector, 'legacy_csv');

    await requestPreview(user);

    const globalSelector = await waitFor(() => {
      const control = findGlobalBalanceActionSelector();
      expect(control).not.toBeNull();
      return control as HTMLSelectElement;
    });

    expect(
      within(globalSelector).getByRole('option', { name: '逐筆決定' }),
    ).toHaveValue('');

    await selectGlobalBalanceAction(user, null);

    const executeButtonBeforeRowDecisions = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });
    expect(executeButtonBeforeRowDecisions).toBeDisabled();
    expect(
      screen.getByText('已選擇「逐筆決定」，請先完成所有短缺列的餘額不足處理方式'),
    ).toBeInTheDocument();

    expect(mockedTransactionApi.executeImport).not.toHaveBeenCalled();

    await selectRowBalanceActionSelection(user, 'ROW-NONE-1', 'Margin');
    await selectRowBalanceActionSelection(user, 'ROW-NONE-2', 'TopUp');
    await selectRowTopUpTransactionType(user, 'ROW-NONE-2', 'Deposit');

    const executeButtonAfterRowDecisions = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });
    expect(executeButtonAfterRowDecisions).toBeEnabled();

    await user.click(executeButtonAfterRowDecisions);

    await waitFor(() => {
      expect(mockedTransactionApi.executeImport).toHaveBeenCalledTimes(1);
    });

    const executeRequest = getLatestExecuteRequest();
    expect(executeRequest.defaultBalanceAction).toBeUndefined();

    const row1 = executeRequest.rows.find((row) => row.rowNumber === 41);
    const row2 = executeRequest.rows.find((row) => row.rowNumber === 42);

    expect(row1).toEqual(
      expect.objectContaining({
        rowNumber: 41,
        balanceAction: 'Margin',
      }),
    );

    expect(row2).toEqual(
      expect.objectContaining({
        rowNumber: 42,
        balanceAction: 'TopUp',
        topUpTransactionType: 'Deposit',
      }),
    );
  });

  it('hardcodes TopUp as broker default, hides balance-action selectors, and still sends defaultBalanceAction', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-default-broker-row-only-actions',
        rows: [
          buildPreviewRow({
            rowNumber: 91,
            rawSecurityName: 'ROW-SHORTFALL-SIGNALED',
            actionsRequired: ['select_balance_action'],
            status: 'requires_user_action',
            balanceDecision: {
              requiredAmount: 500,
              availableBalance: 300,
              shortfall: 200,
              action: null,
              topUpTransactionType: null,
              decisionScope: null,
            },
          }),
          buildPreviewRow({
            rowNumber: 92,
            rawSecurityName: 'ROW-SHORTFALL-UNSIGNALED',
            ticker: '2317',
            actionsRequired: [],
            status: 'valid',
            balanceDecision: null,
          }),
        ],
      }),
    );

    mockedTransactionApi.executeImport.mockResolvedValue(
      buildExecuteResponse({
        summary: {
          totalRows: 2,
          insertedRows: 2,
          failedRows: 0,
          errorCount: 0,
        },
      }),
    );

    render(
      <StockImportButton
        portfolioId={TEST_PORTFOLIO_ID}
        boundLedgerCurrencyCode="USD"
        onImportComplete={vi.fn()}
        onImportSuccess={vi.fn()}
      />,
    );

    const user = userEvent.setup();

    await openImportModalAndUploadCsv(user, buildImportCsvFile());
    await moveToPreviewStep(user);
    await requestPreview(user);

    const previewRequest = getLatestPreviewRequest();
    expect(previewRequest.selectedFormat).toBe('broker_statement');

    expect(findGlobalBalanceActionSelector()).toBeNull();
    expect(() => findRowBalanceActionSelector('ROW-SHORTFALL-SIGNALED')).toThrow(
      '找不到列 ROW-SHORTFALL-SIGNALED 的餘額處理方式下拉',
    );
    expect(screen.getByText('補足餘額')).toBeInTheDocument();

    const executeButton = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });
    expect(executeButton).toBeEnabled();

    await user.click(executeButton);

    await waitFor(() => {
      expect(mockedTransactionApi.executeImport).toHaveBeenCalledTimes(1);
    });

    const executeRequest = getLatestExecuteRequest();
    expect(executeRequest.defaultBalanceAction).toEqual({
      action: 'TopUp',
      topUpTransactionType: 'Deposit',
    });

    const signaledRow = executeRequest.rows.find((row) => row.rowNumber === 91);
    const unsignaledRow = executeRequest.rows.find((row) => row.rowNumber === 92);

    expect(signaledRow).toEqual(
      expect.objectContaining({
        rowNumber: 91,
        balanceAction: 'TopUp',
        topUpTransactionType: 'Deposit',
      }),
    );

    expect(unsignaledRow).toEqual(
      expect.objectContaining({
        rowNumber: 92,
        ticker: '2317',
        confirmedTradeSide: 'buy',
      }),
    );
    expect(unsignaledRow).not.toHaveProperty('balanceAction');
    expect(unsignaledRow).not.toHaveProperty('topUpTransactionType');
  });

  it('uses hardcoded TopUp default even when broker preview omits shortfall signal', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-default-broker-missing-shortfall-signal',
        rows: [
          buildPreviewRow({
            rowNumber: 93,
            rawSecurityName: 'ROW-SHORTFALL-SIGNAL-MISSING',
            actionsRequired: [],
            status: 'valid',
            // Simulate inconsistent preview context: requiredAmount > availableBalance but shortfall signal is omitted.
            balanceDecision: {
              requiredAmount: 600,
              availableBalance: 100,
              shortfall: 0,
              action: null,
              topUpTransactionType: null,
              decisionScope: null,
            },
          }),
        ],
      }),
    );

    mockedTransactionApi.executeImport.mockResolvedValue(buildExecuteResponse());

    render(
      <StockImportButton
        portfolioId={TEST_PORTFOLIO_ID}
        boundLedgerCurrencyCode="USD"
        onImportComplete={vi.fn()}
        onImportSuccess={vi.fn()}
      />,
    );

    const user = userEvent.setup();

    await openImportModalAndUploadCsv(user, buildImportCsvFile());
    await moveToPreviewStep(user);
    await requestPreview(user);

    const previewRequest = getLatestPreviewRequest();
    expect(previewRequest.selectedFormat).toBe('broker_statement');
    expect(findGlobalBalanceActionSelector()).toBeNull();

    const executeButton = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });
    expect(executeButton).toBeEnabled();

    await user.click(executeButton);

    await waitFor(() => {
      expect(mockedTransactionApi.executeImport).toHaveBeenCalledTimes(1);
    });

    const executeRequest = getLatestExecuteRequest();
    expect(executeRequest.defaultBalanceAction).toEqual({
      action: 'TopUp',
      topUpTransactionType: 'Deposit',
    });

    const row = executeRequest.rows.find((candidate) => candidate.rowNumber === 93);
    expect(row).toEqual(
      expect.objectContaining({
        rowNumber: 93,
        confirmedTradeSide: 'buy',
      }),
    );
    expect(row).not.toHaveProperty('balanceAction');
    expect(row).not.toHaveProperty('topUpTransactionType');
  });

  it('blocks execute when row requires ticker input but ticker remains unresolved', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-missing-ticker',
        rows: [
          buildPreviewRow({
            rowNumber: 60,
            rawSecurityName: 'ROW-MISSING-TICKER',
            ticker: null,
            status: 'requires_user_action',
            actionsRequired: ['input_ticker'],
          }),
        ],
      }),
    );

    render(
      <StockImportButton
        portfolioId={TEST_PORTFOLIO_ID}
        boundLedgerCurrencyCode="USD"
        onImportComplete={vi.fn()}
        onImportSuccess={vi.fn()}
      />,
    );

    const user = userEvent.setup();

    await openImportModalAndUploadCsv(user, buildImportCsvFile());
    await moveToPreviewStep(user);
    await requestPreview(user);

    const executeButton = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });

    expect(executeButton).toBeDisabled();
    expect(screen.getByText('仍有列需要手動輸入股票代號')).toBeInTheDocument();
    expect(mockedTransactionApi.executeImport).not.toHaveBeenCalled();
  });

  it('does not block execute for choose_sell_before_buy_handling rows and omits sell-before-buy payload fields', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-sbb-auto',
        rows: [
          buildPreviewRow({
            rowNumber: 61,
            rawSecurityName: 'ROW-SBB-AUTO',
            status: 'requires_user_action',
            usesPartialHistoryAssumption: true,
            actionsRequired: ['choose_sell_before_buy_handling'],
          }),
        ],
      }),
    );

    mockedTransactionApi.executeImport.mockResolvedValue(buildExecuteResponse());

    render(
      <StockImportButton
        portfolioId={TEST_PORTFOLIO_ID}
        boundLedgerCurrencyCode="USD"
        onImportComplete={vi.fn()}
        onImportSuccess={vi.fn()}
      />,
    );

    const user = userEvent.setup();

    await openImportModalAndUploadCsv(user, buildImportCsvFile());
    await moveToPreviewStep(user);
    await requestPreview(user);

    expectNoSellBeforeBuyControls();

    const executeButton = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });
    expect(executeButton).toBeEnabled();

    await user.click(executeButton);

    await waitFor(() => {
      expect(mockedTransactionApi.executeImport).toHaveBeenCalledTimes(1);
    });

    const executeRequest = getLatestExecuteRequest();
    expect(executeRequest).not.toHaveProperty('baselineDecision');

    const row = executeRequest.rows.find((candidate) => candidate.rowNumber === 61);
    expect(row).toBeDefined();
    expect(row).not.toHaveProperty('sellBeforeBuyAction');
  });

  it('keeps preview state and status notice when execute timeout recovers to processing status', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-recover-processing',
        rows: [
          buildPreviewRow({
            rowNumber: 70,
            rawSecurityName: 'ROW-RECOVER-PROCESSING',
            ticker: '2330',
            status: 'valid',
          }),
        ],
      }),
    );

    mockedTransactionApi.executeImport.mockRejectedValue(new Error('execute timeout'));
    mockedTransactionApi.getImportStatus.mockResolvedValue(
      buildExecuteStatusResponse({
        sessionId: 'session-recover-processing',
        executionStatus: 'processing',
        completedAtUtc: null,
        result: null,
      }),
    );

    render(
      <StockImportButton
        portfolioId={TEST_PORTFOLIO_ID}
        boundLedgerCurrencyCode="USD"
        onImportComplete={vi.fn()}
        onImportSuccess={vi.fn()}
      />,
    );

    const user = userEvent.setup();

    await openImportModalAndUploadCsv(user, buildImportCsvFile());
    await moveToPreviewStep(user);
    await requestPreview(user);

    await executeImport(user);

    await waitFor(() => {
      expect(mockedTransactionApi.getImportStatus).toHaveBeenCalledWith('session-recover-processing');
    });

    expect(screen.getByText('匯入處理中，請稍候...')).toBeInTheDocument();
    expect(screen.queryByText(/Session:/i)).not.toBeInTheDocument();
    expect(screen.queryByText('匯入完成')).not.toBeInTheDocument();
    expect(screen.queryByText('匯入完成（部分成功）')).not.toBeInTheDocument();

    const executeButton = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });
    expect(executeButton).toBeEnabled();

    expect(localStorage.getItem(`stock_import_pending_session_${TEST_PORTFOLIO_ID}`)).not.toBeNull();
  });

  it('recovers completed status via getImportStatus after execute failure and clears pending session', async () => {
    const onImportComplete = vi.fn();
    const onImportSuccess = vi.fn();

    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-recover-completed',
        rows: [
          buildPreviewRow({
            rowNumber: 71,
            rawSecurityName: 'ROW-RECOVER-COMPLETED',
            ticker: '2330',
            status: 'valid',
          }),
        ],
      }),
    );

    mockedTransactionApi.executeImport.mockRejectedValue(new Error('execute timeout'));
    mockedTransactionApi.getImportStatus.mockResolvedValue(
      buildExecuteStatusResponse({
        sessionId: 'session-recover-completed',
        executionStatus: 'completed',
        result: buildExecuteResponse({
          sessionId: 'session-recover-completed',
          status: 'committed',
          summary: {
            totalRows: 1,
            insertedRows: 1,
            failedRows: 0,
            errorCount: 0,
          },
        }),
      }),
    );

    render(
      <StockImportButton
        portfolioId={TEST_PORTFOLIO_ID}
        boundLedgerCurrencyCode="USD"
        onImportComplete={onImportComplete}
        onImportSuccess={onImportSuccess}
      />,
    );

    const user = userEvent.setup();

    await openImportModalAndUploadCsv(user, buildImportCsvFile());
    await moveToPreviewStep(user);
    await requestPreview(user);

    await executeImport(user);

    await waitFor(() => {
      expect(mockedTransactionApi.getImportStatus).toHaveBeenCalledWith('session-recover-completed');
    });

    await waitFor(() => {
      expect(onImportComplete).toHaveBeenCalledTimes(1);
    });
    expect(onImportSuccess).toHaveBeenCalledTimes(1);

    expect(localStorage.getItem(`stock_import_pending_session_${TEST_PORTFOLIO_ID}`)).toBeNull();
    expect(await screen.findByText('匯入完成')).toBeInTheDocument();
  });

  it('shows updated Chinese-only balance action copy and removes tax-alias hint text for legacy mode selectors', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-copy-update',
        selectedFormat: 'legacy_csv',
        rows: [
          buildPreviewRow({
            rowNumber: 51,
            rawSecurityName: 'ROW-COPY-CHECK',
            actionsRequired: ['select_balance_action'],
            status: 'requires_user_action',
            balanceDecision: {
              requiredAmount: 300,
              availableBalance: 100,
              shortfall: 200,
              action: null,
              topUpTransactionType: null,
              decisionScope: null,
            },
          }),
        ],
      }),
    );

    render(
      <StockImportButton
        portfolioId={TEST_PORTFOLIO_ID}
        boundLedgerCurrencyCode="USD"
        onImportComplete={vi.fn()}
        onImportSuccess={vi.fn()}
      />,
    );

    const user = userEvent.setup();

    await openImportModalAndUploadCsv(user, buildImportCsvFile());
    await moveToPreviewStep(user);

    const formatSelector = await screen.findByLabelText(/匯入類型/i);
    expect(formatSelector).toBeInTheDocument();
    await user.selectOptions(formatSelector, 'legacy_csv');

    await requestPreview(user);

    expect(screen.getByText('餘額不足預設處理方式')).toBeInTheDocument();
    expect(
      screen.queryByText('餘額不足預設處理方式（選「逐筆決定」時需逐筆設定）'),
    ).not.toBeInTheDocument();

    expect(
      screen.queryByText(
        '交易稅多欄別名（交易稅／稅款／證交稅）會先加總；執行匯入時會再與手續費合併，統一計入交易費用（StockTransaction.Fees）。',
      ),
    ).not.toBeInTheDocument();

    const globalSelector = await waitFor(() => {
      const control = findGlobalBalanceActionSelector();
      expect(control).not.toBeNull();
      return control as HTMLSelectElement;
    });

    expect(within(globalSelector).getByRole('option', { name: '融資' })).toHaveValue('Margin');
    expect(within(globalSelector).getByRole('option', { name: '補足餘額' })).toHaveValue('TopUp');
    expect(within(globalSelector).queryByRole('option', { name: '融資（Margin）' })).not.toBeInTheDocument();
    expect(within(globalSelector).queryByRole('option', { name: '補足餘額（Top-up）' })).not.toBeInTheDocument();

    const rowSelector = findRowBalanceActionSelector('ROW-COPY-CHECK');
    expect(within(rowSelector).getByRole('option', { name: '融資' })).toHaveValue('Margin');
    expect(within(rowSelector).getByRole('option', { name: '補足餘額' })).toHaveValue('TopUp');
    expect(within(rowSelector).queryByRole('option', { name: '融資（Margin）' })).not.toBeInTheDocument();
    expect(within(rowSelector).queryByRole('option', { name: '補足餘額（Top-up）' })).not.toBeInTheDocument();
  });

  it('hides baseline tooltip heading and trigger in broker mode while keeping sell-before-buy UI hidden', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-tooltip-a11y',
        rows: [
          buildPreviewRow({
            rowNumber: 80,
            rawSecurityName: 'ROW-TOOLTIP-A11Y',
            status: 'requires_user_action',
            usesPartialHistoryAssumption: true,
            actionsRequired: ['choose_sell_before_buy_handling'],
          }),
        ],
      }),
    );

    render(
      <StockImportButton
        portfolioId={TEST_PORTFOLIO_ID}
        boundLedgerCurrencyCode="USD"
        onImportComplete={vi.fn()}
        onImportSuccess={vi.fn()}
      />,
    );

    const user = userEvent.setup();

    await openImportModalAndUploadCsv(user, buildImportCsvFile());
    await moveToPreviewStep(user);

    expect(screen.queryByText('目前持倉基準')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('欄位說明：目前持倉基準')).not.toBeInTheDocument();
    expect(
      screen.queryByText('此日期僅用於描述目前持倉快照（預設今天）；券商匯入錨點會由系統依最早成交日自動回推 1 天。'),
    ).not.toBeInTheDocument();

    expectNoSellBeforeBuyControls();
  });

  it('updates baseline copy and keeps wider preview layout classes without sell-before-buy UI', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-copy-layout',
        rows: [
          buildPreviewRow({
            rowNumber: 81,
            rawSecurityName: 'ROW-COPY-LAYOUT',
            status: 'requires_user_action',
            usesPartialHistoryAssumption: true,
            actionsRequired: ['choose_sell_before_buy_handling'],
          }),
        ],
      }),
    );

    render(
      <StockImportButton
        portfolioId={TEST_PORTFOLIO_ID}
        boundLedgerCurrencyCode="USD"
        onImportComplete={vi.fn()}
        onImportSuccess={vi.fn()}
      />,
    );

    const user = userEvent.setup();

    await openImportModalAndUploadCsv(user, buildImportCsvFile());
    await moveToPreviewStep(user);
    await requestPreview(user);

    expect(screen.queryByLabelText('目前持倉現金餘額')).not.toBeInTheDocument();
    const openingLedgerInput = screen.getByLabelText('目前用於投資的閒置資金（帳本餘額）');

    expect(openingLedgerInput).toHaveAttribute('placeholder', '可空');
    expect(screen.getByText('目前持倉')).toBeInTheDocument();
    expect(screen.queryByText('目前持倉基準')).not.toBeInTheDocument();
    expect(screen.queryByText('目前持倉日期（預設今天）')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('目前持倉基準日')).not.toBeInTheDocument();
    expect(screen.getByText('目前用於投資的閒置資金（帳本餘額）')).toBeInTheDocument();
    expect(
      screen.queryByText('此日期僅用於描述目前持倉快照（預設今天）；券商匯入錨點會由系統依最早成交日自動回推 1 天。'),
    ).not.toBeInTheDocument();

    expect(screen.queryByLabelText('欄位說明：目前持倉基準')).not.toBeInTheDocument();

    expect(screen.queryByText('期初現金餘額（可空）')).not.toBeInTheDocument();
    expect(screen.queryByText('期初帳本餘額（可空）')).not.toBeInTheDocument();
    expect(screen.queryByText('期初持倉（可多筆）')).not.toBeInTheDocument();

    expectNoSellBeforeBuyControls();

    const modalHeading = screen.getByRole('heading', { name: '匯入股票交易' });
    const modalContainer = modalHeading.closest('.card-dark');
    expect(modalContainer).not.toBeNull();
    expect(modalContainer as HTMLElement).toHaveClass('max-w-6xl');

    const remediationTable = screen.getByRole('table');
    expect(remediationTable).toHaveClass('min-w-[960px]');
  });
});
