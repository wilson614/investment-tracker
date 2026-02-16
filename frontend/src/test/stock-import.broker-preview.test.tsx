import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
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
const TEST_PORTFOLIO_ID = 'portfolio-broker-preview';

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
  const labeled = screen.queryByLabelText(/格式|format/i);
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

    const hasLegacy = optionTexts.some((text) => /legacy|舊/.test(text));
    const hasBroker = optionTexts.some((text) => /broker|對帳|證券/.test(text));

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

  const matcher = targetFormat === 'legacy_csv'
    ? /legacy|舊/
    : /broker|對帳|證券/;

  const option = Array.from(selector.options).find((candidate) =>
    matcher.test(`${candidate.value} ${candidate.textContent ?? ''}`.toLowerCase()),
  );

  if (!option) {
    throw new Error(`找不到格式選項: ${targetFormat}`);
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

async function confirmTradeSideForRow(
  user: ReturnType<typeof userEvent.setup>,
  rowMarker: string,
  targetSide: 'buy' | 'sell',
) {
  const sideMatcher = targetSide === 'buy' ? /買|buy/i : /賣|sell/i;
  const rowContainer = getRowContainer(rowMarker);

  const radio = within(rowContainer).queryByRole('radio', { name: sideMatcher });
  if (radio) {
    await user.click(radio);
    return;
  }

  const button = within(rowContainer).queryByRole('button', { name: sideMatcher });
  if (button) {
    await user.click(button);
    return;
  }

  const select = within(rowContainer).queryByRole('combobox') as HTMLSelectElement | null;
  if (select instanceof HTMLSelectElement) {
    const option = Array.from(select.options).find((candidate) =>
      sideMatcher.test(`${candidate.value} ${candidate.textContent ?? ''}`),
    );

    if (!option) {
      throw new Error(`找不到每列買賣方向選項: ${targetSide}`);
    }

    await user.selectOptions(select, option.value);
    return;
  }

  throw new Error('找不到每列買賣方向控制項');
}

function expectVisibleTextOrder(textsInExpectedOrder: string[]) {
  const nodes = textsInExpectedOrder.map((text) => screen.getByText(text));

  for (let index = 0; index < nodes.length - 1; index++) {
    const relation = nodes[index].compareDocumentPosition(nodes[index + 1]);
    expect(relation & Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(0);
  }
}

describe('Stock import broker preview flow', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubGlobal('alert', vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('broker format detection can be manually overridden before re-preview', async () => {
    mockedTransactionApi.previewImport
      .mockResolvedValueOnce(
        buildPreviewResponse({
          sessionId: 'session-format-1',
          detectedFormat: 'broker_statement',
          selectedFormat: 'broker_statement',
          rows: [buildPreviewRow({ rawSecurityName: 'ROW-FORMAT-1' })],
        }),
      )
      .mockResolvedValueOnce(
        buildPreviewResponse({
          sessionId: 'session-format-2',
          detectedFormat: 'broker_statement',
          selectedFormat: 'legacy_csv',
          rows: [buildPreviewRow({ rawSecurityName: 'ROW-FORMAT-1' })],
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

    await openImportModalAndUploadCsv(user, buildImportCsvFile());
    await moveToPreviewStep(user);
    await requestPreview(user);

    const firstPreviewRequest = getLatestPreviewRequest();
    expect(firstPreviewRequest).toEqual(
      expect.objectContaining({
        portfolioId: TEST_PORTFOLIO_ID,
        selectedFormat: 'broker_statement',
      }),
    );
    expect(firstPreviewRequest.csvContent).toContain('代碼');

    await selectImportFormat(user, 'legacy_csv');
    await requestPreview(user);

    const secondPreviewRequest = getLatestPreviewRequest();
    expect(secondPreviewRequest.selectedFormat).toBe('legacy_csv');
  });

  it('ambiguous-side rows require per-row confirmation and submit confirmed side in execute payload', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-ambiguous',
        rows: [
          buildPreviewRow({
            rowNumber: 7,
            rawSecurityName: 'ROW-AMBIGUOUS',
            ticker: 'AMBG',
            tradeSide: 'ambiguous',
            confirmedTradeSide: null,
            status: 'requires_user_action',
            actionsRequired: ['confirm_trade_side'],
            netSettlement: null,
          }),
          buildPreviewRow({
            rowNumber: 8,
            rawSecurityName: 'ROW-VALID',
            ticker: '2330',
            tradeSide: 'buy',
            confirmedTradeSide: 'buy',
            status: 'valid',
            actionsRequired: [],
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
        onImportComplete={vi.fn()}
        onImportSuccess={vi.fn()}
      />,
    );

    const user = userEvent.setup();

    await openImportModalAndUploadCsv(user, buildImportCsvFile());
    await moveToPreviewStep(user);
    await requestPreview(user);

    await screen.findByText('ROW-AMBIGUOUS');

    const executeButtonBeforeConfirm = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });
    expect(executeButtonBeforeConfirm).toBeDisabled();

    await confirmTradeSideForRow(user, 'ROW-AMBIGUOUS', 'sell');

    const executeButtonAfterConfirm = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });
    expect(executeButtonAfterConfirm).toBeEnabled();

    await user.click(executeButtonAfterConfirm);

    await waitFor(() => {
      expect(mockedTransactionApi.executeImport).toHaveBeenCalledTimes(1);
    });

    const executeRequest = getLatestExecuteRequest();
    expect(executeRequest).toEqual(
      expect.objectContaining({
        sessionId: 'session-ambiguous',
        portfolioId: TEST_PORTFOLIO_ID,
      }),
    );

    expect(executeRequest.rows).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          rowNumber: 7,
          confirmedTradeSide: 'sell',
        }),
      ]),
    );
  });

  it('preview row ordering remains stable after per-row confirmation interaction', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-ordering',
        rows: [
          buildPreviewRow({
            rowNumber: 3,
            rawSecurityName: 'ROW-03',
            ticker: '3003',
            tradeSide: 'ambiguous',
            confirmedTradeSide: null,
            status: 'requires_user_action',
            actionsRequired: ['confirm_trade_side'],
            netSettlement: null,
          }),
          buildPreviewRow({
            rowNumber: 1,
            rawSecurityName: 'ROW-01',
            ticker: '1001',
          }),
          buildPreviewRow({
            rowNumber: 2,
            rawSecurityName: 'ROW-02',
            ticker: '2002',
          }),
        ],
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

    await openImportModalAndUploadCsv(user, buildImportCsvFile());
    await moveToPreviewStep(user);
    await requestPreview(user);

    await screen.findByText('ROW-03');
    expectVisibleTextOrder(['ROW-03', 'ROW-01', 'ROW-02']);

    await confirmTradeSideForRow(user, 'ROW-03', 'buy');

    expectVisibleTextOrder(['ROW-03', 'ROW-01', 'ROW-02']);
  });
});
