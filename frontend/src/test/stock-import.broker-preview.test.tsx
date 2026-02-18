import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import userEvent from '@testing-library/user-event';
import { useEffect, useState } from 'react';
import { StockImportButton } from '../components/import/StockImportButton';
import { portfolioApi, transactionApi } from '../services/api';
import type {
  StockImportDiagnostic,
  StockImportExecuteRequest,
  StockImportExecuteResponse,
  StockImportPreviewRequest,
  StockImportPreviewResponse,
  StockImportPreviewRow,
  StockImportSelectedFormat,
  YearPerformance,
} from '../types';

vi.mock('../services/api', () => ({
  portfolioApi: {
    getAvailableYears: vi.fn(),
    calculateYearPerformance: vi.fn(),
  },
  transactionApi: {
    create: vi.fn(),
    previewImport: vi.fn(),
    executeImport: vi.fn(),
  },
}));

const mockedPortfolioApi = vi.mocked(portfolioApi, { deep: true });
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

function buildBrokerStatementCsvFile(rows: string[] = [], fileName = 'stock-import-broker.csv'): File {
  const headers = '股名,日期,成交股數,淨收付,成交單價,成交價金,手續費,交易稅,幣別,備註';
  const sampleRows = rows.length > 0
    ? rows
    : ['"台積電","2026/01/22","1,000","-625,010","625","625,000","10","0","台幣",""'];

  const content = `${headers}\n${sampleRows.join('\n')}`;
  const file = new File([content], fileName, { type: 'text/csv' });

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
  errors?: StockImportPreviewResponse['errors'];
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
        transactionId: 'tx-1',
        message: 'Created',
      },
    ],
    errors: [],
    ...overrides,
  };
}

function buildYearPerformance(overrides: Partial<YearPerformance> = {}): YearPerformance {
  const year = overrides.year ?? new Date().getFullYear();

  return {
    year,
    xirr: 0.1234,
    xirrPercentage: 12.34,
    totalReturnPercentage: 10.11,
    modifiedDietzPercentage: 9.87,
    timeWeightedReturnPercentage: 8.76,
    startValueHome: 100000,
    endValueHome: 120000,
    netContributionsHome: 20000,
    sourceCurrency: 'TWD',
    xirrSource: 0.1234,
    xirrPercentageSource: 12.34,
    totalReturnPercentageSource: 10.11,
    modifiedDietzPercentageSource: 9.87,
    timeWeightedReturnPercentageSource: 8.76,
    startValueSource: 100000,
    endValueSource: 120000,
    netContributionsSource: 20000,
    cashFlowCount: 2,
    transactionCount: 1,
    earliestTransactionDateInYear: `${year}-01-22`,
    missingPrices: [],
    isComplete: true,
    ...overrides,
  };
}

function buildDiagnostic(overrides: Partial<StockImportDiagnostic> = {}): StockImportDiagnostic {
  return {
    rowNumber: 1,
    fieldName: 'unknownField',
    invalidValue: null,
    errorCode: 'UNKNOWN_ERROR',
    message: 'Something went wrong',
    correctionGuidance: 'Please check input data',
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

function formatPercent(value: number | null): string {
  if (value == null) {
    return '-';
  }

  const sign = value >= 0 ? '+' : '';
  return `${sign}${value.toLocaleString('zh-TW', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}%`;
}

function formatHomeCurrency(value: number | null | undefined): string {
  if (value == null) {
    return '-';
  }

  return Math.round(value).toLocaleString('zh-TW');
}

function ImportToPerformanceHarness() {
  const [importCompleted, setImportCompleted] = useState(false);
  const [performance, setPerformance] = useState<YearPerformance | null>(null);

  useEffect(() => {
    if (!importCompleted) {
      return;
    }

    let isCancelled = false;

    const loadPerformance = async () => {
      const years = await portfolioApi.getAvailableYears(TEST_PORTFOLIO_ID);
      const fallbackYear = years.currentYear ?? years.years[0] ?? null;

      if (fallbackYear == null) {
        if (!isCancelled) {
          setPerformance(null);
        }
        return;
      }

      const result = await portfolioApi.calculateYearPerformance(TEST_PORTFOLIO_ID, {
        year: fallbackYear,
      });

      if (!isCancelled) {
        setPerformance(result);
      }
    };

    void loadPerformance();

    return () => {
      isCancelled = true;
    };
  }, [importCompleted]);

  return (
    <div>
      <StockImportButton
        portfolioId={TEST_PORTFOLIO_ID}
        onImportComplete={() => setImportCompleted(true)}
        onImportSuccess={() => undefined}
      />

      {importCompleted && performance ? (
        <section aria-label="performance-summary">
          <div>
            <p>年化報酬率 (XIRR)</p>
            <p>{formatPercent(performance.xirrPercentage)}</p>
          </div>
          <div>
            <p>淨投入</p>
            <p>{`${formatHomeCurrency(performance.netContributionsHome)} ${performance.sourceCurrency ?? 'TWD'}`}</p>
          </div>
        </section>
      ) : null}
    </div>
  );
}

describe('Stock import broker preview flow', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubGlobal('alert', vi.fn());

    mockedPortfolioApi.getAvailableYears.mockResolvedValue({
      years: [new Date().getFullYear()],
      earliestYear: new Date().getFullYear(),
      currentYear: new Date().getFullYear(),
    });
    mockedPortfolioApi.calculateYearPerformance.mockResolvedValue(buildYearPerformance());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('broker format detection can be manually overridden before re-preview', async () => {
    mockedTransactionApi.previewImport
      .mockResolvedValueOnce(
        buildPreviewResponse({
          sessionId: 'session-format-1',
          detectedFormat: 'legacy_csv',
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
    expect(screen.getByText('系統偵測：一般')).toBeInTheDocument();

    const executeButtonBeforeRepreview = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });
    expect(executeButtonBeforeRepreview).toBeEnabled();

    await selectImportFormat(user, 'legacy_csv');

    const executeButtonAfterFormatChange = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });
    expect(executeButtonAfterFormatChange).toBeDisabled();
    expect(screen.getByText('請先產生預覽')).toBeInTheDocument();

    await requestPreview(user);

    const secondPreviewRequest = getLatestPreviewRequest();
    expect(secondPreviewRequest.selectedFormat).toBe('legacy_csv');
    expect(screen.getByText('系統偵測：券商')).toBeInTheDocument();

    const executeButtonAfterRepreview = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });
    expect(executeButtonAfterRepreview).toBeEnabled();
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

  it('broker statement local detection hides mapping selectors before preview and uses renamed format labels', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-kgi-mapping-hidden',
        detectedFormat: 'broker_statement',
        selectedFormat: 'broker_statement',
        rows: [buildPreviewRow({ rawSecurityName: 'ROW-BROKER-HIDDEN' })],
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

    await openImportModalAndUploadCsv(user, buildBrokerStatementCsvFile());

    await moveToPreviewStep(user);

    const formatSelector = await waitFor(() => {
      const selector = findFormatSelector();
      expect(selector).not.toBeNull();
      return selector as HTMLSelectElement;
    });

    expect(Array.from(formatSelector.options).map((option) => option.textContent)).toEqual(
      expect.arrayContaining(['券商', '一般']),
    );

    await requestPreview(user);

    expect(screen.getByText('系統偵測：券商')).toBeInTheDocument();
    expect(screen.queryByText('系統偵測：未知格式')).not.toBeInTheDocument();
  });

  it('tw stock csv mapping should not include market/currency/exchange rate selectors', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-tw-mapping-fields',
        detectedFormat: 'legacy_csv',
        selectedFormat: 'legacy_csv',
        rows: [buildPreviewRow({ rawSecurityName: 'ROW-TW-MAPPING' })],
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

    expect(screen.queryByText(/市場\s*\*/)).not.toBeInTheDocument();
    expect(screen.queryByText(/幣別\s*\*/)).not.toBeInTheDocument();
    expect(screen.queryByText(/匯率（選填）\s*\*/)).not.toBeInTheDocument();

    await moveToPreviewStep(user);
    await selectImportFormat(user, 'legacy_csv');
    await requestPreview(user);

    const latestPreviewRequest = getLatestPreviewRequest();
    expect(latestPreviewRequest.selectedFormat).toBe('legacy_csv');
  });

  it('disables execute when preview contains blocking errors and does not call execute API', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-blocking-errors',
        rows: [buildPreviewRow({ rowNumber: 1, rawSecurityName: 'ROW-BLOCKING-ERROR' })],
        errors: [
          buildDiagnostic({
            rowNumber: 1,
            fieldName: 'netSettlement',
            invalidValue: null,
            errorCode: 'CSV_HEADER_MISSING',
            message: 'CSV_HEADER_MISSING: required header netSettlement is missing',
            correctionGuidance: 'Please provide broker statement netSettlement column before preview.',
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

    const executeButton = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });
    expect(executeButton).toBeDisabled();
    expect(screen.getByText('預覽有錯誤，請先修正')).toBeInTheDocument();
    expect(screen.getByText(/required header netSettlement is missing/i)).toBeInTheDocument();
    expect(
      screen.getByText(/建議：Please provide broker statement netSettlement column before preview\./i),
    ).toBeInTheDocument();
    expect(screen.getByText(/代碼：CSV_HEADER_MISSING/i)).toBeInTheDocument();

    await user.click(executeButton);
    expect(mockedTransactionApi.executeImport).not.toHaveBeenCalled();
  });

  it('shows clearer diagnostics for backend-specific errors (message, guidance, code, invalid value)', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-backend-diagnostic-preview',
        rows: [buildPreviewRow({ rowNumber: 5, rawSecurityName: 'ROW-BACKEND-DIAGNOSTIC' })],
        errors: [
          buildDiagnostic({
            rowNumber: 5,
            fieldName: 'ticker',
            invalidValue: '??',
            errorCode: 'TICKER_NOT_RECOGNIZED',
            message: 'Ticker cannot be resolved for this broker statement row',
            correctionGuidance: 'Please provide a valid ticker or map a correct security name.',
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

    expect(screen.getByText(/Ticker cannot be resolved for this broker statement row/i)).toBeInTheDocument();
    expect(
      screen.getByText(/建議：Please provide a valid ticker or map a correct security name\./i),
    ).toBeInTheDocument();
    expect(screen.getByText(/代碼：TICKER_NOT_RECOGNIZED/i)).toBeInTheDocument();
    expect(screen.getByText(/輸入值：\?\?/i)).toBeInTheDocument();
  });

  it('disables execute when preview rows are empty and does not call execute API', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-empty-rows',
        rows: [],
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

    const executeButton = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });
    expect(executeButton).toBeDisabled();
    expect(screen.getByText('預覽無可匯入資料')).toBeInTheDocument();

    await user.click(executeButton);
    expect(mockedTransactionApi.executeImport).not.toHaveBeenCalled();
  });

  it('completes import from 證券app匯出範例.csv and renders performance metrics from mocked performance APIs', async () => {
    mockedTransactionApi.previewImport.mockResolvedValue(
      buildPreviewResponse({
        sessionId: 'session-performance-flow',
        detectedFormat: 'broker_statement',
        selectedFormat: 'broker_statement',
        rows: [
          buildPreviewRow({
            rowNumber: 1,
            rawSecurityName: 'ROW-PERFORMANCE',
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
      }),
    );

    const performanceYear = 2025;
    mockedPortfolioApi.getAvailableYears.mockResolvedValue({
      years: [performanceYear, performanceYear - 1],
      earliestYear: performanceYear - 1,
      currentYear: performanceYear,
    });
    mockedPortfolioApi.calculateYearPerformance.mockResolvedValue(
      buildYearPerformance({
        year: performanceYear,
        xirrPercentage: 17.65,
        netContributionsHome: 45678.6,
        sourceCurrency: 'USD',
      }),
    );

    render(<ImportToPerformanceHarness />);

    const user = userEvent.setup();

    expect(screen.queryByText('年化報酬率 (XIRR)')).not.toBeInTheDocument();
    expect(screen.queryByText('淨投入')).not.toBeInTheDocument();

    await openImportModalAndUploadCsv(
      user,
      buildBrokerStatementCsvFile([], '證券app匯出範例.csv'),
    );
    await moveToPreviewStep(user);
    await requestPreview(user);

    const previewRequest = getLatestPreviewRequest();
    expect(previewRequest).toEqual(
      expect.objectContaining({
        portfolioId: TEST_PORTFOLIO_ID,
        selectedFormat: 'broker_statement',
      }),
    );
    expect(previewRequest.csvContent).toContain('淨收付');

    const executeButton = await screen.findByRole('button', {
      name: /確認匯入|執行匯入|開始匯入/i,
    });
    expect(executeButton).toBeEnabled();

    await user.click(executeButton);

    await waitFor(() => {
      expect(mockedTransactionApi.executeImport).toHaveBeenCalledTimes(1);
    });

    await waitFor(() => {
      expect(mockedPortfolioApi.getAvailableYears).toHaveBeenCalledWith(TEST_PORTFOLIO_ID);
    });

    await waitFor(() => {
      expect(mockedPortfolioApi.calculateYearPerformance).toHaveBeenCalledWith(
        TEST_PORTFOLIO_ID,
        { year: performanceYear },
      );
    });

    const performanceSummary = await screen.findByLabelText('performance-summary');

    expect(within(performanceSummary).getByText('年化報酬率 (XIRR)')).toBeInTheDocument();
    expect(within(performanceSummary).getByText('+17.65%')).toBeInTheDocument();
    expect(within(performanceSummary).getByText('淨投入')).toBeInTheDocument();
    expect(within(performanceSummary).getByText('45,679 USD')).toBeInTheDocument();

    expect(within(performanceSummary).queryByText('+9.87%')).not.toBeInTheDocument();
    expect(within(performanceSummary).queryByText('300 TWD')).not.toBeInTheDocument();
    expect(within(performanceSummary).queryByText('—')).not.toBeInTheDocument();
    expect(within(performanceSummary).queryByText('-', { exact: true })).not.toBeInTheDocument();
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
