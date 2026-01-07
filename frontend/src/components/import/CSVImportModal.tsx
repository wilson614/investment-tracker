import { useState, useCallback } from 'react';
import { X, Upload, FileText, AlertCircle, CheckCircle, ChevronDown, ChevronUp } from 'lucide-react';
import {
  parseCSV,
  autoMapColumns,
  type ParsedCSV,
  type ColumnMapping,
  type ParseError,
} from '../../utils/csvParser';

export interface FieldDefinition {
  name: string;
  label: string;
  aliases: string[];
  required: boolean;
}

interface CSVImportModalProps {
  isOpen: boolean;
  onClose: () => void;
  title: string;
  fields: FieldDefinition[];
  onImport: (
    data: ParsedCSV,
    mapping: ColumnMapping
  ) => Promise<{ success: boolean; errors: ParseError[] }>;
}

export function CSVImportModal({
  isOpen,
  onClose,
  title,
  fields,
  onImport,
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

  const handleMappingChange = (fieldName: string, csvHeader: string | null) => {
    setMapping((prev) => ({
      ...prev,
      [fieldName]: csvHeader === '' ? null : csvHeader,
    }));
  };

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
    if (validateMapping()) {
      setStep('preview');
    }
  };

  const handleImport = async () => {
    if (!csvData) return;

    setIsImporting(true);
    try {
      const result = await onImport(csvData, mapping);
      setImportResult({
        success: result.success,
        errors: result.errors,
        successCount: csvData.rowCount - result.errors.length,
      });
      setStep('result');
    } catch (err) {
      alert(`匯入失敗：${err instanceof Error ? err.message : '未知錯誤'}`);
    } finally {
      setIsImporting(false);
    }
  };

  const handleClose = () => {
    setStep('upload');
    setCsvData(null);
    setMapping({});
    setImportResult(null);
    setShowErrors(false);
    onClose();
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50">
      <div className="card-dark rounded-xl shadow-xl w-full max-w-2xl max-h-[90vh] overflow-hidden flex flex-col">
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
                          field.required && !mapping[field.name]
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
                    <span>查看錯誤詳情 ({importResult.errors.length} 筆)</span>
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
        <div className="px-6 py-4 border-t border-[var(--border-color)] flex justify-between">
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
              <button
                onClick={handleImport}
                disabled={isImporting}
                className="btn-accent disabled:opacity-50"
              >
                {isImporting ? '匯入中...' : '確認匯入'}
              </button>
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
