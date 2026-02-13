/**
 * CurrencyImportButton
 *
 * 外幣交易 CSV 匯入按鈕：使用通用 `CSVImportModal` 解析 CSV，
 * 再轉成 backend atomic import contract 所需的 multipart/form-data 一次提交。
 */
import { useState } from 'react';
import { Upload } from 'lucide-react';
import { CSVImportModal, type FieldDefinition } from './CSVImportModal';
import {
  getRowValue,
  type ParsedCSV,
  type ColumnMapping,
  type ParseError,
} from '../../utils/csvParser';

interface CurrencyImportButtonProps {
  /** 目標 ledger ID */
  ledgerId: string;
  /** 匯入完成後 callback （通常用於重新載入頁面資料） */
  onImportComplete: () => void;
  /** 若提供，改用自訂 trigger （常用於搭配 FileDropdown） */
  renderTrigger?: (onClick: () => void) => React.ReactNode;
}

interface AtomicImportDiagnostic {
  rowNumber: number;
  fieldName?: string;
  invalidValue?: string;
  errorCode?: string;
  message: string;
  correctionGuidance?: string;
}

interface AtomicImportResponse {
  status: 'committed' | 'rejected';
  summary: {
    totalRows: number;
    insertedRows: number;
    rejectedRows: number;
    errorCount?: number;
  };
  errors?: AtomicImportDiagnostic[];
  diagnostics?: AtomicImportDiagnostic[];
}

const API_BASE_URL = import.meta.env.VITE_API_URL || '/api';
const IMPORT_FILE_NAME = 'currency-transactions-import.csv';

// Field definitions for currency transaction CSV
const currencyFields: FieldDefinition[] = [
  {
    name: 'date',
    label: '日期',
    aliases: ['transactionDate', 'transaction_date', 'Date', '交易日期', '日期'],
    required: true,
  },
  {
    name: 'type',
    label: '交易類型',
    aliases: ['transactionType', 'transaction_type', 'Type', '類型', '種類'],
    required: true,
  },
  {
    name: 'foreignAmount',
    label: '外幣金額',
    aliases: ['foreign_amount', 'ForeignAmount', 'amount', 'Amount', '外幣', '金額', '外幣金額', '外幣金額(USD)', 'foreignamount(usd)'],
    required: true,
  },
  {
    name: 'homeAmount',
    label: '台幣金額',
    aliases: ['home_amount', 'HomeAmount', 'twdAmount', 'TWDAmount', '台幣', 'TWD', '台幣金額', '台幣金額(TWD)', 'homeamount(twd)'],
    required: false,
  },
  {
    name: 'exchangeRate',
    label: '匯率',
    aliases: ['exchange_rate', 'ExchangeRate', 'rate', 'Rate', 'exchange rate', '匯率', '兌換匯率'],
    required: false,
  },
  {
    name: 'notes',
    label: '備註',
    aliases: ['Notes', 'memo', 'Memo', 'description', 'Description', '備註', '說明'],
    required: false,
  },
];

const exportColumns: Array<{ field: keyof ColumnMapping | string; header: string }> = [
  { field: 'date', header: 'transactionDate' },
  { field: 'type', header: 'transactionType' },
  { field: 'foreignAmount', header: 'foreignAmount' },
  { field: 'homeAmount', header: 'homeAmount' },
  { field: 'exchangeRate', header: 'exchangeRate' },
  { field: 'notes', header: 'notes' },
];

function escapeCsvValue(value: string): string {
  if (/[",\n\r]/.test(value)) {
    return `"${value.replace(/"/g, '""')}"`;
  }
  return value;
}

function buildAtomicImportCsv(csvData: ParsedCSV, mapping: ColumnMapping): string {
  const headerRow = exportColumns.map((column) => column.header).join(',');

  const dataRows = csvData.rows.map((row) => {
    return exportColumns
      .map((column) => {
        const value = getRowValue(row, csvData.headers, mapping, column.field as string) ?? '';
        return escapeCsvValue(value.trim());
      })
      .join(',');
  });

  return [headerRow, ...dataRows].join('\n');
}

function extractAtomicImportDiagnostics(result: Partial<AtomicImportResponse> | null): AtomicImportDiagnostic[] {
  if (!result || typeof result !== 'object') {
    return [];
  }

  if (Array.isArray(result.errors)) {
    return result.errors;
  }

  if (Array.isArray(result.diagnostics)) {
    return result.diagnostics;
  }

  return [];
}

function mapAtomicImportDiagnostics(diagnostics: AtomicImportDiagnostic[]): ParseError[] {
  return diagnostics.map((diagnostic) => {
    const parts: string[] = [diagnostic.message];

    if (diagnostic.invalidValue && diagnostic.invalidValue.trim().length > 0) {
      parts.push(`錯誤值：${diagnostic.invalidValue}`);
    }

    if (diagnostic.correctionGuidance) {
      parts.push(`修正建議：${diagnostic.correctionGuidance}`);
    }

    return {
      // Backend contract rowNumber 已含 header row offset（首筆資料列為 2）
      row: Math.max(2, diagnostic.rowNumber),
      column: diagnostic.fieldName,
      message: parts.join('；'),
    };
  });
}

function ensureAtomicFailureErrors(parsedErrors: ParseError[], totalRows: number): ParseError[] {
  const existingRows = new Set(parsedErrors.map((error) => error.row));
  if (existingRows.size >= totalRows) {
    return parsedErrors;
  }

  const rollbackErrors: ParseError[] = [];

  for (let row = 2; row <= totalRows + 1; row++) {
    if (existingRows.has(row)) {
      continue;
    }

    rollbackErrors.push({
      row,
      message: '此列因原子匯入規則未提交，請先修正其他列錯誤後再重新匯入',
    });
  }

  return [...parsedErrors, ...rollbackErrors];
}

function parseApiErrorMessage(raw: unknown): string {
  if (typeof raw === 'string' && raw.trim()) {
    return raw.trim();
  }

  if (!raw || typeof raw !== 'object') {
    return '匯入失敗';
  }

  const maybe = raw as Record<string, unknown>;
  if (typeof maybe.error === 'string' && maybe.error.trim()) {
    return maybe.error;
  }
  if (typeof maybe.message === 'string' && maybe.message.trim()) {
    return maybe.message;
  }
  if (typeof maybe.title === 'string' && maybe.title.trim()) {
    return maybe.title;
  }

  return '匯入失敗';
}

export function CurrencyImportButton({
  ledgerId,
  onImportComplete,
  renderTrigger,
}: CurrencyImportButtonProps) {
  const [isModalOpen, setIsModalOpen] = useState(false);

  /**
   * 開啟匯入 Modal。
   */
  const handleOpenImport = () => setIsModalOpen(true);

  /**
   * 實際匯入：前端僅做欄位映射，並交由 backend atomic import 一次驗證與提交。
   */
  const handleImport = async (
    csvData: ParsedCSV,
    mapping: ColumnMapping
  ): Promise<{ success: boolean; errors: ParseError[] }> => {
    try {
      const csvContent = buildAtomicImportCsv(csvData, mapping);

      const formData = new FormData();
      formData.append('ledgerId', ledgerId);
      formData.append(
        'file',
        new Blob([csvContent], { type: 'text/csv;charset=utf-8' }),
        IMPORT_FILE_NAME
      );

      const token = localStorage.getItem('token');
      const headers = new Headers();
      if (token) {
        headers.set('Authorization', `Bearer ${token}`);
      }

      const response = await fetch(`${API_BASE_URL}/currencytransactions/import`, {
        method: 'POST',
        headers,
        body: formData,
      });

      const responseText = await response.text();
      const trimmedResponseText = responseText.trim();
      let payload: AtomicImportResponse | Record<string, unknown> | string | null = null;

      if (trimmedResponseText.length > 0) {
        try {
          payload = JSON.parse(trimmedResponseText) as AtomicImportResponse | Record<string, unknown>;
        } catch {
          payload = trimmedResponseText;
        }
      }

      if (response.ok) {
        const result = payload as AtomicImportResponse | null;

        if (result?.status === 'committed') {
          if (result.summary.insertedRows > 0) {
            onImportComplete();
          }

          return {
            success: true,
            errors: [],
          };
        }

        if (result?.status === 'rejected') {
          const parsedErrors = mapAtomicImportDiagnostics(extractAtomicImportDiagnostics(result));
          return {
            success: false,
            errors: ensureAtomicFailureErrors(parsedErrors, csvData.rowCount),
          };
        }

        return {
          success: false,
          errors: [
            {
              row: 2,
              message: '匯入失敗：後端回傳了未知的匯入狀態',
            },
          ],
        };
      }

      if (response.status === 422 && payload && typeof payload === 'object') {
        const rejected = payload as Partial<AtomicImportResponse>;
        const parsedErrors = mapAtomicImportDiagnostics(extractAtomicImportDiagnostics(rejected));

        return {
          success: false,
          errors: ensureAtomicFailureErrors(parsedErrors, csvData.rowCount),
        };
      }

      return {
        success: false,
        errors: [
          {
            row: 2,
            message: parseApiErrorMessage(payload),
          },
        ],
      };
    } catch (err) {
      return {
        success: false,
        errors: [
          {
            row: 2,
            message: err instanceof Error ? err.message : '匯入失敗',
          },
        ],
      };
    }
  };

  return (
    <>
      {renderTrigger ? (
        renderTrigger(handleOpenImport)
      ) : (
        <button
          onClick={handleOpenImport}
          className="btn-dark flex items-center gap-2 px-3 py-1.5 text-sm"
        >
          <Upload className="w-3.5 h-3.5" />
          匯入
        </button>
      )}

      <CSVImportModal
        isOpen={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        title="匯入外幣交易"
        fields={currencyFields}
        onImport={handleImport}
      />
    </>
  );
}
