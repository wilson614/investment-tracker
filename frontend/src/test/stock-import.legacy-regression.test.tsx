import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import userEvent from '@testing-library/user-event';
import { StockImportButton } from '../components/import/StockImportButton';
import { transactionApi } from '../services/api';
import type {
  StockImportExecuteRequest,
  StockImportExecuteResponse,
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
  },
}));

const mockedTransactionApi = vi.mocked(transactionApi, { deep: true });
const TEST_PORTFOLIO_ID = 'portfolio-legacy-regression';

function buildLegacyCsvFile(rows: string[] = []): File {
  const headers = 'transactionDate,Ticker,transactionType,Shares,pricePerShare,Fees,Market,Currency,Notes';
  const sampleRows = rows.length > 0
    ? rows
    : ['"2026-01-22","2330","Buy","1000","625","10","TW","TWD","legacy-sample"'];

  const content = `${headers}\n${sampleRows.join('\n')}`;
  const file = new File([content], 'stock-import-legacy.csv', { type: 'text/csv' });

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
    rawSecurityName: 'LEGACY-ROW-DEFAULT',
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
  errors?: StockImportPreviewResponse['errors'];
} = {}): StockImportPreviewResponse {
  const rows = params.rows ?? [buildPreviewRow()];

  return {
    sessionId: params.sessionId ?? 'session-legacy-preview',
    detectedFormat: params.detectedFormat ?? 'legacy_csv',
    selectedFormat: params.selectedFormat ?? 'legacy_csv',
    summary: {
      totalRows: rows.length,
      validRows: rows.filter((row) => row.status === 'valid').length,
      requiresActionRows: rows.filter((row) => row.status === 'requires_user_action').length,
      invalidRows: rows.filter((row) => row.status === 'invalid').length,
    },
    rows,
    errors: params.errors ?? [],
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
        transactionId: 'tx-legacy-1',
        message: 'Created',
      },
    ],
    errors: [],
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

function findFormatSelector(): HTMLSelectElement | null {
  const labeled = screen.queryByLabelText(/類型|格式|format/i);
  if (labeled instanceof HTMLSelectElement) {
    return labeled;
  }

  const comboboxes = screen
    .queryAllByRole('combobox')
    .filter((node): node is HTMLSelectElement => node instanceof HTMLSelectElement);

  return comboboxes.find((select) => {
    const optionTexts = Array.from(select.options).map((option) =>
      `${option.value} ${option.textContent ?? ''}`.toLowerCase(),
    );

    const hasLegacy = optionTexts.some((text) => /legacy_csv|legacy|一般|舊/.test(text));
    const hasBroker = optionTexts.some((text) => /broker_statement|broker|券商|對帳|證券/.test(text));

    return hasLegacy && hasBroker;
  }) ?? null;
}

async function selectImportFormat(
  user: ReturnType<typeof userEvent.setup>,
  targetFormat: StockImportSelectedFormat,
) {
  const selector = await waitFor(() => {
    const control = findFormatSelector();
    expect(control).not.toBeNull();
    return control as HTMLSelectElement;
  });

  const option = Array.from(selector.options).find((candidate) => candidate.value === targetFormat);

  if (!option) {
    throw new Error(`找不到格式選項: ${targetFormat}`);
  }

  await user.selectOptions(selector, option.value);
}

async function executeImport(user: ReturnType<typeof userEvent.setup>) {
  const executeButton = await screen.findByRole('button', {
    name: /確認匯入|執行匯入|開始匯入/i,
  });

  await user.click(executeButton);
}

describe('Stock import legacy regression flow', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubGlobal('alert', vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('legacy format still supports preview and execute through unified import flow', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-legacy-unified',
        detectedFormat: 'legacy_csv',
        selectedFormat: 'legacy_csv',
        rows: [
          buildPreviewRow({
            rowNumber: 12,
            rawSecurityName: 'LEGACY-ROW-12',
            ticker: '2330',
            tradeSide: 'buy',
            confirmedTradeSide: 'buy',
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
        results: [
          {
            rowNumber: 12,
            success: true,
            transactionId: 'tx-legacy-12',
            message: 'Created',
          },
        ],
      }),
    );

    const onImportComplete = vi.fn();
    const onImportSuccess = vi.fn();

    render(
      <StockImportButton
        portfolioId={TEST_PORTFOLIO_ID}
        onImportComplete={onImportComplete}
        onImportSuccess={onImportSuccess}
      />,
    );

    const user = userEvent.setup();

    await openImportModalAndUploadCsv(user, buildLegacyCsvFile());
    await moveToPreviewStep(user);

    await selectImportFormat(user, 'legacy_csv');
    await requestPreview(user);

    const previewRequest = getLatestPreviewRequest();
    expect(previewRequest).toEqual(
      expect.objectContaining({
        portfolioId: TEST_PORTFOLIO_ID,
        selectedFormat: 'legacy_csv',
      }),
    );
    expect(previewRequest.csvContent).toContain('transactionDate');
    expect(screen.getByText('系統偵測：一般')).toBeInTheDocument();

    await executeImport(user);

    await waitFor(() => {
      expect(mockedTransactionApi.executeImport).toHaveBeenCalledTimes(1);
    });

    expect(mockedTransactionApi.create).not.toHaveBeenCalled();

    const executeRequest = getLatestExecuteRequest();
    expect(executeRequest).toEqual(
      expect.objectContaining({
        sessionId: 'session-legacy-unified',
        portfolioId: TEST_PORTFOLIO_ID,
      }),
    );
    expect(executeRequest.rows).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          rowNumber: 12,
          ticker: '2330',
          confirmedTradeSide: 'buy',
        }),
      ]),
    );

    expect(onImportComplete).toHaveBeenCalledTimes(1);
    expect(onImportSuccess).toHaveBeenCalledTimes(1);
  });

  it('unknown detected format should follow local detection when csv clearly matches legacy sample', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-legacy-unknown-fallback',
        detectedFormat: 'unknown',
        selectedFormat: 'legacy_csv',
        rows: [buildPreviewRow({ rowNumber: 3, rawSecurityName: 'LEGACY-UNKNOWN-ROW' })],
      }),
    );

    render(
      <StockImportButton
        portfolioId={TEST_PORTFOLIO_ID}
        onImportComplete={vi.fn()}
        onImportSuccess={vi.fn()}
      />,
    );

    const user = userEvent.setup();

    await openImportModalAndUploadCsv(user, buildLegacyCsvFile());
    await moveToPreviewStep(user);
    await requestPreview(user);

    expect(screen.getByText('系統偵測：一般')).toBeInTheDocument();
    expect(screen.queryByText('系統偵測：未知格式')).not.toBeInTheDocument();
  });

  it('manual broker override on detected legacy CSV surfaces header error and allows switching back to legacy format', async () => {
    mockedTransactionApi.previewImport
      .mockResolvedValueOnce(
        buildPreviewResponse({
          sessionId: 'session-legacy-format-1',
          detectedFormat: 'legacy_csv',
          selectedFormat: 'broker_statement',
          rows: [buildPreviewRow({ rowNumber: 15, rawSecurityName: 'STALE-PREVIEW-ROW' })],
          errors: [
            {
              rowNumber: 1,
              fieldName: 'netSettlement',
              invalidValue: null,
              errorCode: 'CSV_HEADER_MISSING',
              message: 'CSV_HEADER_MISSING: required header netSettlement is missing',
              correctionGuidance: 'Please provide broker statement netSettlement column before preview.',
            },
          ],
        }),
      )
      .mockResolvedValueOnce(
        buildPreviewResponse({
          sessionId: 'session-legacy-format-2',
          detectedFormat: 'legacy_csv',
          selectedFormat: 'legacy_csv',
          rows: [buildPreviewRow({ rowNumber: 21, rawSecurityName: 'LEGACY-FORMAT-ROW' })],
        }),
      );

    render(
      <StockImportButton
        portfolioId={TEST_PORTFOLIO_ID}
        onImportComplete={vi.fn()}
        onImportSuccess={vi.fn()}
      />,
    );

    const user = userEvent.setup();

    await openImportModalAndUploadCsv(user, buildLegacyCsvFile());
    await moveToPreviewStep(user);

    await requestPreview(user);

    const firstPreviewRequest = getLatestPreviewRequest();
    expect(firstPreviewRequest.selectedFormat).toBe('broker_statement');
    expect(screen.getByText('系統偵測：一般')).toBeInTheDocument();
    expect(await screen.findByText('STALE-PREVIEW-ROW')).toBeInTheDocument();

    const brokerOverrideError = await screen.findByText(/CSV_HEADER_MISSING/i);
    expect(brokerOverrideError).toHaveTextContent(/第\s*\d+\s*行/);
    expect(brokerOverrideError).toHaveTextContent(/\(netSettlement\)/i);

    const executeButtonBeforeFormatChange = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });
    expect(executeButtonBeforeFormatChange).toBeDisabled();
    expect(screen.getByText('預覽有錯誤，請先修正')).toBeInTheDocument();

    await selectImportFormat(user, 'legacy_csv');

    expect(screen.queryByText('STALE-PREVIEW-ROW')).not.toBeInTheDocument();

    const executeButtonAfterFormatChange = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });
    expect(executeButtonAfterFormatChange).toBeDisabled();
    expect(screen.getByText('請先產生預覽')).toBeInTheDocument();

    await requestPreview(user);

    const secondPreviewRequest = getLatestPreviewRequest();
    expect(secondPreviewRequest.selectedFormat).toBe('legacy_csv');
    expect(await screen.findByText('LEGACY-FORMAT-ROW')).toBeInTheDocument();
  });

  it('legacy execute result preserves row-level error mapping assumptions', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-legacy-row-mapping',
        detectedFormat: 'legacy_csv',
        selectedFormat: 'legacy_csv',
        rows: [
          buildPreviewRow({ rowNumber: 1, rawSecurityName: 'LEGACY-OK-ROW', ticker: '2330' }),
          buildPreviewRow({ rowNumber: 2, rawSecurityName: 'LEGACY-ERROR-ROW', ticker: 'FAIL' }),
        ],
      }),
    );

    mockedTransactionApi.executeImport.mockResolvedValue(
      buildExecuteResponse({
        status: 'partially_committed',
        summary: {
          totalRows: 2,
          insertedRows: 1,
          failedRows: 1,
          errorCount: 1,
        },
        results: [
          {
            rowNumber: 1,
            success: true,
            transactionId: 'tx-legacy-ok',
            message: 'Created',
          },
          {
            rowNumber: 2,
            success: false,
            errorCode: 'SYMBOL_UNRESOLVED',
            message: 'Result-level fallback message',
          },
        ],
        errors: [
          {
            rowNumber: 2,
            fieldName: 'Ticker',
            invalidValue: 'FAIL',
            errorCode: 'SYMBOL_UNRESOLVED',
            message: 'Security identity cannot be resolved uniquely',
            correctionGuidance: 'Enter ticker manually or exclude this row',
          },
        ],
      }),
    );

    const onImportComplete = vi.fn();
    const onImportSuccess = vi.fn();

    render(
      <StockImportButton
        portfolioId={TEST_PORTFOLIO_ID}
        onImportComplete={onImportComplete}
        onImportSuccess={onImportSuccess}
      />,
    );

    const user = userEvent.setup();

    await openImportModalAndUploadCsv(user, buildLegacyCsvFile([
      '"2026-01-22","2330","Buy","1000","625","10","TW","TWD","ok-row"',
      '"2026-01-23","FAIL","Buy","100","10","1","TW","TWD","error-row"',
    ]));
    await moveToPreviewStep(user);

    await selectImportFormat(user, 'legacy_csv');
    await requestPreview(user);

    await executeImport(user);

    await screen.findByText('匯入完成（部分成功）');

    expect(onImportComplete).toHaveBeenCalledTimes(1);
    expect(onImportSuccess).toHaveBeenCalledTimes(1);

    const errorToggleButton = await screen.findByRole('button', { name: '查看錯誤詳情（1 筆）' });
    await user.click(errorToggleButton);

    const rowLabel = screen.getByText('第 2 行', { selector: 'span' });
    const errorRow = rowLabel.closest('div');
    expect(errorRow).not.toBeNull();
    expect(errorRow).toHaveTextContent(/\(Ticker\)/);
    expect(errorRow).toHaveTextContent(/Security identity cannot be resolved uniquely/);
    expect(errorRow).toHaveTextContent(/Enter ticker manually or exclude this row/);
  });

});
