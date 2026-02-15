/**
 * CSVImportModal
 *
 * 通用 CSV 匯入 Modal：提供「上傳 → 欄位對應 → 預覽 → 匯入結果」流程。
 * 可透過可選 props 擴充為 preview/execute 雙階段，以及 unresolved row remediation UI。
 */
import { useState, useCallback } from 'react';
import { X, Upload, FileText, AlertCircle, CheckCircle, ChevronDown, ChevronUp, Loader2 } from 'lucide-react';
import {
  parseCSV,
  autoMapColumns,
  type ParsedCSV,
  type ColumnMapping,
  type ParseError,
} from '../../utils/csvParser';

export interface FieldDefinition {
  /** 欄位 key（提供給 mapping 使用） */
  name: string;
  /** UI 顯示名稱 */
  label: string;
  /** CSV 欄位可能出現的別名（用於 auto mapping） */
  aliases: string[];
  /** 是否必填 */
  required: boolean;
}

export interface CSVImportActionResult {
  success: boolean;
  errors: ParseError[];
  successCount?: number;
}

export interface CSVImportFormatOption {
  value: string;
  label: string;
}

export interface CSVImportSummaryItem {
  key: string;
  label: string;
  value: number | string;
}

export interface CSVImportRemediationRow {
  rowNumber: number;
  rawSecurityName: string | null;
  ticker: string | null;
  displayTicker?: string | null;
  status: 'valid' | 'requires_user_action' | 'invalid';
  requiresTickerInput?: boolean;
  manualTicker?: string | null;
  tradeSide?: 'buy' | 'sell' | 'ambiguous';
  confirmedTradeSide?: 'buy' | 'sell' | null;
  requiresTradeSideConfirmation?: boolean;
  note?: string | null;
}

export interface CSVImportPreviewExtensions {
  formatLabel?: string;
  selectedFormat?: string;
  detectedFormatLabel?: string | null;
  formatOptions?: CSVImportFormatOption[];
  onChangeFormat?: (nextValue: string) => void;

  previewButtonLabel?: string;
  isPreviewing?: boolean;
  hasPreview?: boolean;

  summaryItems?: CSVImportSummaryItem[];
  remediationRows?: CSVImportRemediationRow[];
  previewErrors?: ParseError[];

  onManualTickerChange?: (rowNumber: number, value: string) => void;
  onChangeTradeSide?: (rowNumber: number, side: 'buy' | 'sell') => void;

  executeDisabled?: boolean;
  executeDisabledReason?: string | null;
}

interface CSVImportModalProps {
  /** 是否顯示 Modal */
  isOpen: boolean;
  /** 關閉 callback（外層負責切換 isOpen） */
  onClose: () => void;
  /** Modal 標題 */
  title: string;
  /** 欄位定義（決定 mapping UI 與必填檢查） */
  fields: FieldDefinition[];
  /**
   * 既有單步匯入流程（保留 legacy 相容）
   */
  onImport?: (
    data: ParsedCSV,
    mapping: ColumnMapping
  ) => Promise<CSVImportActionResult>;
  /**
   * 可選：先產生 preview，再執行 execute 的流程。
   */
  onRequestPreview?: (
    data: ParsedCSV,
    mapping: ColumnMapping
  ) => Promise<void>;
  /**
   * 可選：preview/execute 流程的 execute handler。
   * 若未提供，會退回使用 onImport。
   */
  onExecute?: (
    data: ParsedCSV,
    mapping: ColumnMapping
  ) => Promise<CSVImportActionResult>;
  /** 可選：預覽步驟擴充內容（format selector / unresolved rows / diagnostics） */
  previewExtensions?: CSVImportPreviewExtensions;
}

function getTradeSideLabel(side: 'buy' | 'sell' | 'ambiguous' | null | undefined): string {
  if (side === 'buy') return '買入';
  if (side === 'sell') return '賣出';
  if (side === 'ambiguous') return '待確認';
  return '-';
}

function getStatusLabel(status: CSVImportRemediationRow['status']): string {
  if (status === 'valid') return '可匯入';
  if (status === 'requires_user_action') return '需補充';
  return '無法匯入';
}

export function CSVImportModal({
  isOpen,
  onClose,
  title,
  fields,
  onImport,
  onRequestPreview,
  onExecute,
  previewExtensions,
}: CSVImportModalProps) {
  const [step, setStep] = useState<'upload' | 'mapping' | 'preview' | 'result'>('upload');
  const [csvData, setCsvData] = useState<ParsedCSV | null>(null);
  const [mapping, setMapping] = useState<ColumnMapping>({});
  const [isImporting, setIsImporting] = useState(false);
  const [importResult, setImportResult] = useState<{
    success: boolean;
    errors: ParseError[];
    successCount: number;
  } | null>(null);
  const [showErrors, setShowErrors] = useState(false);

  /**
   * 上傳檔案後：讀取文字 → parse CSV → 自動欄位對應 → 進入 mapping step。
   */
  const handleFileSelect = useCallback(
    async (event: React.ChangeEvent<HTMLInputElement>) => {
      const file = event.target.files?.[0];
      if (!file) return;

      try {
        const content = await file.text();
        const parsed = parseCSV(content);
        setCsvData(parsed);

        // Auto-map columns
        const autoMapping = autoMapColumns(parsed.headers, fields);
        setMapping(autoMapping);
        setStep('mapping');
      } catch {
        alert('讀取檔案失敗，請確認檔案格式正確。');
      }
    },
    [fields]
  );

  /**
   * 更新單一欄位對應。
   *
   * 注意：`''` 代表未選擇，會轉成 `null`。
   */
  const handleMappingChange = (fieldName: string, csvHeader: string | null) => {
    setMapping((prev) => ({
      ...prev,
      [fieldName]: csvHeader === '' ? null : csvHeader,
    }));
  };

  /**
   * 驗證必填欄位是否都有完成對應。
   */
  const validateMapping = (): boolean => {
    const requiredFields = fields.filter((f) => f.required);
    const missingFields = requiredFields.filter((f) => !mapping[f.name]);

    if (missingFields.length > 0) {
      alert(`請對應以下必填欄位：${missingFields.map((f) => f.label).join('、')}`);
      return false;
    }

    return true;
  };

  const handleProceedToPreview = () => {
    // preview/execute 模式允許先進入 preview，再由 backend 驗證欄位與格式
    if (onRequestPreview || validateMapping()) {
      setStep('preview');
    }
  };

  const handleRequestPreview = async () => {
    if (!csvData || !onRequestPreview) return;

    try {
      await onRequestPreview(csvData, mapping);
    } catch (err) {
      alert(`產生預覽失敗：${err instanceof Error ? err.message : '未知錯誤'}`);
    }
  };

  /**
   * 執行匯入：
   * - default：呼叫 onImport
   * - preview/execute：呼叫 onExecute（若有）
   */
  const handleImport = async () => {
    if (!csvData) return;

    const runner = onExecute ?? onImport;
    if (!runner) {
      alert('匯入設定錯誤：未提供匯入處理函式');
      return;
    }

    setIsImporting(true);
    try {
      const result = await runner(csvData, mapping);
      setImportResult({
        success: result.success,
        errors: result.errors,
        successCount: result.successCount ?? csvData.rowCount - result.errors.length,
      });
      setStep('result');
    } catch (err) {
      alert(`匯入失敗：${err instanceof Error ? err.message : '未知錯誤'}`);
    } finally {
      setIsImporting(false);
    }
  };

  /**
   * 關閉 Modal 並清理內部狀態，確保下次開啟從 upload step 開始。
   */
  const handleClose = () => {
    setStep('upload');
    setCsvData(null);
    setMapping({});
    setImportResult(null);
    setShowErrors(false);
    onClose();
  };

  if (!isOpen) return null;

  const hasCustomPreview = Boolean(onRequestPreview);
  const hasPreviewData = previewExtensions?.hasPreview ?? false;
  const isPreviewing = previewExtensions?.isPreviewing ?? false;
  const executeDisabled =
    isImporting ||
    isPreviewing ||
    (hasCustomPreview && !hasPreviewData) ||
    Boolean(previewExtensions?.executeDisabled);

  return (
    <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50">
      <div className="card-dark rounded-xl shadow-xl w-full max-w-4xl max-h-[90vh] overflow-hidden flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-[var(--border-color)]">
          <h2 className="text-xl font-semibold text-[var(--text-primary)]">{title}</h2>
          <button
            onClick={handleClose}
            className="p-2 text-[var(--text-muted)] hover:text-[var(--text-primary)] hover:bg-[var(--bg-hover)] rounded-lg transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto p-6">
          {step === 'upload' && (
            <div className="text-center py-12">
              <Upload className="w-16 h-16 text-[var(--text-muted)] mx-auto mb-4" />
              <h3 className="text-lg font-medium text-[var(--text-primary)] mb-2">
                上傳 CSV 檔案
              </h3>
              <p className="text-[var(--text-muted)] mb-6">
                選擇包含交易紀錄的 CSV 檔案，系統會自動偵測欄位對應
              </p>
              <label className="inline-block">
                <input
                  type="file"
                  accept=".csv"
                  onChange={handleFileSelect}
                  className="hidden"
                />
                <span className="btn-accent cursor-pointer">
                  選擇檔案
                </span>
              </label>
            </div>
          )}

          {step === 'mapping' && csvData && (
            <div className="space-y-6">
              <div className="flex items-center gap-3 text-[var(--color-success)] bg-[var(--color-success-soft)] p-4 rounded-lg">
                <FileText className="w-5 h-5" />
                <span>
                  已讀取 <strong>{csvData.rowCount}</strong> 筆資料，共{' '}
                  <strong>{csvData.headers.length}</strong> 個欄位
                </span>
              </div>

              <div>
                <h3 className="text-lg font-medium text-[var(--text-primary)] mb-4">
                  欄位對應
                </h3>
                <div className="space-y-3">
                  {fields.map((field) => (
                    <div
                      key={field.name}
                      className="flex items-center gap-4"
                    >
                      <div className="w-40 flex-shrink-0">
                        <span className="text-[var(--text-secondary)] font-medium">
                          {field.label}
                        </span>
                        {field.required && (
                          <span className="text-[var(--color-danger)] ml-1">*</span>
                        )}
                      </div>
                      <select
                        value={mapping[field.name] || ''}
                        onChange={(e) =>
                          handleMappingChange(field.name, e.target.value)
                        }
                        className={`flex-1 input-dark ${
                          !onRequestPreview && field.required && !mapping[field.name]
                            ? 'border-[var(--color-danger)]'
                            : ''
                        }`}
                      >
                        <option value="">-- 選擇欄位 --</option>
                        {csvData.headers.map((header) => (
                          <option key={header} value={header}>
                            {header}
                          </option>
                        ))}
                      </select>
                    </div>
                  ))}
                </div>
              </div>

              <div className="bg-[var(--bg-secondary)] p-4 rounded-lg">
                <h4 className="font-medium text-[var(--text-primary)] mb-2">資料預覽（前 3 筆）</h4>
                <div className="overflow-x-auto">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="border-b border-[var(--border-color)]">
                        {csvData.headers.map((header) => (
                          <th
                            key={header}
                            className="px-3 py-2 text-left text-[var(--text-muted)] font-medium"
                          >
                            {header}
                          </th>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      {csvData.rows.slice(0, 3).map((row, idx) => (
                        <tr key={idx} className="border-b border-[var(--border-color)]">
                          {row.map((cell, cellIdx) => (
                            <td
                              key={cellIdx}
                              className="px-3 py-2 text-[var(--text-secondary)]"
                            >
                              {cell || '-'}
                            </td>
                          ))}
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
          )}

          {step === 'preview' && csvData && (
            <div className="space-y-6">
              <div className="bg-[var(--accent-butter-soft)] p-4 rounded-lg">
                <h4 className="font-medium text-[var(--text-primary)] mb-2">確認匯入</h4>
                <p className="text-[var(--text-secondary)]">
                  即將匯入 <strong>{csvData.rowCount}</strong> 筆交易紀錄
                </p>
              </div>

              {previewExtensions?.formatOptions && previewExtensions.selectedFormat && (
                <div className="bg-[var(--bg-secondary)] p-4 rounded-lg space-y-2">
                  <label className="text-sm font-medium text-[var(--text-primary)]" htmlFor="import-format-selector">
                    {previewExtensions.formatLabel ?? '匯入格式'}
                  </label>
                  <select
                    id="import-format-selector"
                    aria-label={previewExtensions.formatLabel ?? '匯入格式'}
                    value={previewExtensions.selectedFormat}
                    onChange={(event) => previewExtensions.onChangeFormat?.(event.target.value)}
                    className="input-dark w-full"
                  >
                    {previewExtensions.formatOptions.map((option) => (
                      <option key={option.value} value={option.value}>
                        {option.label}
                      </option>
                    ))}
                  </select>
                  {previewExtensions.detectedFormatLabel && (
                    <p className="text-sm text-[var(--text-muted)]">{previewExtensions.detectedFormatLabel}</p>
                  )}
                </div>
              )}

              {previewExtensions?.summaryItems && previewExtensions.summaryItems.length > 0 && (
                <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
                  {previewExtensions.summaryItems.map((item) => (
                    <div key={item.key} className="bg-[var(--bg-secondary)] rounded-lg p-3">
                      <p className="text-xs text-[var(--text-muted)]">{item.label}</p>
                      <p className="text-lg font-semibold text-[var(--text-primary)]">{item.value}</p>
                    </div>
                  ))}
                </div>
              )}

              <div>
                <h4 className="font-medium text-[var(--text-primary)] mb-2">欄位對應摘要</h4>
                <div className="bg-[var(--bg-secondary)] p-4 rounded-lg space-y-2">
                  {fields.map((field) => (
                    <div key={field.name} className="flex justify-between text-sm">
                      <span className="text-[var(--text-muted)]">{field.label}</span>
                      <span className="font-medium text-[var(--text-primary)]">
                        {mapping[field.name] || (
                          <span className="text-[var(--text-muted)]">未對應</span>
                        )}
                      </span>
                    </div>
                  ))}
                </div>
              </div>

              {previewExtensions?.previewErrors && previewExtensions.previewErrors.length > 0 && (
                <div className="bg-[var(--color-danger-soft)] p-4 rounded-lg max-h-60 overflow-y-auto">
                  {previewExtensions.previewErrors.map((error, idx) => (
                    <div
                      key={`${error.row}-${error.column ?? ''}-${idx}`}
                      className="text-sm text-[var(--color-danger)] py-1 border-b border-[var(--color-danger)]/20 last:border-0"
                    >
                      <span className="font-medium">第 {error.row} 行</span>
                      {error.column && ` (${error.column})`}: {error.message}
                    </div>
                  ))}
                </div>
              )}

              {previewExtensions?.remediationRows && previewExtensions.remediationRows.length > 0 && (
                <div className="border border-[var(--border-color)] rounded-lg overflow-hidden">
                  <div className="overflow-x-auto">
                    <table className="w-full text-sm min-w-[820px]">
                      <thead>
                        <tr className="border-b border-[var(--border-color)] bg-[var(--bg-secondary)]">
                          <th className="px-3 py-2 text-left text-[var(--text-muted)] font-medium">列號</th>
                          <th className="px-3 py-2 text-left text-[var(--text-muted)] font-medium">標的名稱</th>
                          <th className="px-3 py-2 text-left text-[var(--text-muted)] font-medium">股票代號</th>
                          <th className="px-3 py-2 text-left text-[var(--text-muted)] font-medium">買賣方向</th>
                          <th className="px-3 py-2 text-left text-[var(--text-muted)] font-medium">狀態</th>
                        </tr>
                      </thead>
                      <tbody>
                        {previewExtensions.remediationRows.map((row) => (
                          <tr key={row.rowNumber} className="border-b border-[var(--border-color)] last:border-0 align-top">
                            <td className="px-3 py-3 text-[var(--text-secondary)]">{row.rowNumber}</td>
                            <td className="px-3 py-3 text-[var(--text-primary)]">{row.rawSecurityName ?? '-'}</td>
                            <td className="px-3 py-3 text-[var(--text-secondary)]">
                              {row.requiresTickerInput ? (
                                <input
                                  type="text"
                                  value={row.manualTicker ?? ''}
                                  onChange={(event) =>
                                    previewExtensions.onManualTickerChange?.(row.rowNumber, event.target.value)
                                  }
                                  placeholder="請輸入 ticker"
                                  aria-label={`第 ${row.rowNumber} 行股票代號`}
                                  className="input-dark w-44"
                                />
                              ) : (
                                row.displayTicker ?? row.ticker ?? '-'
                              )}
                            </td>
                            <td className="px-3 py-3 text-[var(--text-secondary)]">
                              {row.requiresTradeSideConfirmation ? (
                                <div className="flex items-center gap-4">
                                  <label className="inline-flex items-center gap-1.5 cursor-pointer text-[var(--text-primary)]">
                                    <input
                                      type="radio"
                                      name={`trade-side-${row.rowNumber}`}
                                      checked={row.confirmedTradeSide === 'buy'}
                                      onChange={() => previewExtensions.onChangeTradeSide?.(row.rowNumber, 'buy')}
                                    />
                                    <span>買入</span>
                                  </label>
                                  <label className="inline-flex items-center gap-1.5 cursor-pointer text-[var(--text-primary)]">
                                    <input
                                      type="radio"
                                      name={`trade-side-${row.rowNumber}`}
                                      checked={row.confirmedTradeSide === 'sell'}
                                      onChange={() => previewExtensions.onChangeTradeSide?.(row.rowNumber, 'sell')}
                                    />
                                    <span>賣出</span>
                                  </label>
                                </div>
                              ) : (
                                getTradeSideLabel(row.confirmedTradeSide ?? row.tradeSide)
                              )}
                              {row.note && (
                                <p className="mt-1 text-xs text-[var(--text-muted)]">{row.note}</p>
                              )}
                            </td>
                            <td className="px-3 py-3 text-[var(--text-secondary)]">{getStatusLabel(row.status)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}

              {previewExtensions?.executeDisabledReason && executeDisabled && (
                <p className="text-sm text-[var(--color-warning)]">
                  {previewExtensions.executeDisabledReason}
                </p>
              )}
            </div>
          )}

          {step === 'result' && importResult && (
            <div className="space-y-6">
              {importResult.success ? (
                <div className="text-center py-8">
                  <CheckCircle className="w-16 h-16 text-[var(--color-success)] mx-auto mb-4" />
                  <h3 className="text-xl font-medium text-[var(--text-primary)] mb-2">
                    匯入完成
                  </h3>
                  <p className="text-[var(--text-muted)]">
                    成功匯入 <strong>{importResult.successCount}</strong> 筆資料
                  </p>
                </div>
              ) : (
                <div className="text-center py-8">
                  <AlertCircle className="w-16 h-16 text-[var(--color-warning)] mx-auto mb-4" />
                  <h3 className="text-xl font-medium text-[var(--text-primary)] mb-2">
                    匯入完成（部分成功）
                  </h3>
                  <p className="text-[var(--text-muted)] mb-4">
                    成功 <strong>{importResult.successCount}</strong> 筆，
                    失敗 <strong>{importResult.errors.length}</strong> 筆
                  </p>
                </div>
              )}

              {importResult.errors.length > 0 && (
                <div>
                  <button
                    onClick={() => setShowErrors(!showErrors)}
                    className="flex items-center gap-2 text-[var(--text-muted)] hover:text-[var(--text-primary)]"
                  >
                    {showErrors ? (
                      <ChevronUp className="w-4 h-4" />
                    ) : (
                      <ChevronDown className="w-4 h-4" />
                    )}
                    <span>查看錯誤詳情（{importResult.errors.length} 筆）</span>
                  </button>

                  {showErrors && (
                    <div className="mt-4 bg-[var(--color-danger-soft)] p-4 rounded-lg max-h-60 overflow-y-auto">
                      {importResult.errors.map((error, idx) => (
                        <div
                          key={idx}
                          className="text-sm text-[var(--color-danger)] py-1 border-b border-[var(--color-danger)]/20 last:border-0"
                        >
                          <span className="font-medium">第 {error.row} 行</span>
                          {error.column && ` (${error.column})`}: {error.message}
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              )}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="px-6 py-4 border-t border-[var(--border-color)] flex justify-between gap-2">
          {step === 'upload' && (
            <button
              onClick={handleClose}
              className="px-4 py-2 text-[var(--text-muted)] hover:bg-[var(--bg-hover)] rounded-lg transition-colors"
            >
              取消
            </button>
          )}

          {step === 'mapping' && (
            <>
              <button
                onClick={() => setStep('upload')}
                className="px-4 py-2 text-[var(--text-muted)] hover:bg-[var(--bg-hover)] rounded-lg transition-colors"
              >
                上一步
              </button>
              <button
                onClick={handleProceedToPreview}
                className="btn-accent"
              >
                下一步
              </button>
            </>
          )}

          {step === 'preview' && (
            <>
              <button
                onClick={() => setStep('mapping')}
                className="px-4 py-2 text-[var(--text-muted)] hover:bg-[var(--bg-hover)] rounded-lg transition-colors"
              >
                上一步
              </button>

              <div className="ml-auto flex items-center gap-2">
                {hasCustomPreview && (
                  <button
                    onClick={handleRequestPreview}
                    disabled={isPreviewing || isImporting}
                    className="btn-dark disabled:opacity-50 inline-flex items-center gap-2"
                  >
                    {(isPreviewing || isImporting) && <Loader2 className="w-4 h-4 animate-spin" />}
                    {previewExtensions?.previewButtonLabel ?? (hasPreviewData ? '重新預覽' : '產生預覽')}
                  </button>
                )}

                <button
                  onClick={handleImport}
                  disabled={executeDisabled}
                  className="btn-accent disabled:opacity-50"
                >
                  {isImporting ? '匯入中...' : '確認匯入'}
                </button>
              </div>
            </>
          )}

          {step === 'result' && (
            <button
              onClick={handleClose}
              className="ml-auto btn-accent"
            >
              完成
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
