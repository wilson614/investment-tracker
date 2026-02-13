import { useEffect, useMemo, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { X, FileText, Loader2, AlertCircle } from 'lucide-react';
import { fetchApi } from '../../services/api';
import { parseCSV, type ParsedCSV } from '../../utils/csvParser';
import { BANK_ACCOUNTS_QUERY_KEY } from '../../features/bank-accounts/hooks/useBankAccounts';
import { ASSETS_KEYS } from '../../features/total-assets/hooks/useTotalAssets';
import { invalidatePerformanceAndAssetsCaches } from '../../utils/cacheInvalidation';
import { useToast } from '../common';

interface BankAccountImportModalProps {
  isOpen: boolean;
  onClose: () => void;
  file: File;
}

type ImportAction = 'create' | 'update' | 'skip';

interface ImportPreviewItemResult {
  bankName: string;
  action: string;
  changeDetails: string[];
}

interface ImportPreviewResult {
  items: ImportPreviewItemResult[];
  validationErrors: string[];
}

interface ImportExecuteResult {
  createdCount: number;
  updatedCount: number;
  skippedCount: number;
}

const ACTION_META: Record<
  ImportAction,
  { label: string; badgeClassName: string; summaryClassName: string }
> = {
  create: {
    label: '新增',
    badgeClassName: 'bg-[var(--color-success-soft)] text-[var(--color-success)]',
    summaryClassName: 'bg-[var(--color-success-soft)] text-[var(--color-success)]',
  },
  update: {
    label: '更新',
    badgeClassName: 'bg-[var(--color-warning-soft)] text-[var(--color-warning)]',
    summaryClassName: 'bg-[var(--color-warning-soft)] text-[var(--color-warning)]',
  },
  skip: {
    label: '略過',
    badgeClassName: 'bg-[var(--bg-hover)] text-[var(--text-muted)]',
    summaryClassName: 'bg-[var(--bg-hover)] text-[var(--text-secondary)]',
  },
};

function normalizeAction(action: string): ImportAction {
  const normalized = action.trim().toLowerCase();
  if (normalized === 'create') return 'create';
  if (normalized === 'update') return 'update';
  return 'skip';
}

function readFileContent(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();

    reader.onload = () => {
      if (typeof reader.result !== 'string') {
        reject(new Error('無法讀取檔案內容'));
        return;
      }

      resolve(reader.result);
    };

    reader.onerror = () => {
      reject(new Error('讀取檔案失敗，請確認檔案格式正確。'));
    };

    reader.readAsText(file);
  });
}

export function BankAccountImportModal({ isOpen, onClose, file }: BankAccountImportModalProps) {
  const queryClient = useQueryClient();
  const toast = useToast();

  const [isParsing, setIsParsing] = useState(false);
  const [isPreviewLoading, setIsPreviewLoading] = useState(false);
  const [isExecuting, setIsExecuting] = useState(false);

  const [csvContent, setCsvContent] = useState('');
  const [parsedCsv, setParsedCsv] = useState<ParsedCSV | null>(null);
  const [previewResult, setPreviewResult] = useState<ImportPreviewResult | null>(null);

  const [parseError, setParseError] = useState<string | null>(null);
  const [apiError, setApiError] = useState<string | null>(null);

  const previewSummary = useMemo(() => {
    const summary = { create: 0, update: 0, skip: 0 };

    if (!previewResult) {
      return summary;
    }

    previewResult.items.forEach((item) => {
      const action = normalizeAction(item.action);
      summary[action] += 1;
    });

    return summary;
  }, [previewResult]);

  useEffect(() => {
    if (!isOpen) return;

    let isActive = true;

    const parseFile = async () => {
      setIsParsing(true);
      setParseError(null);
      setApiError(null);
      setPreviewResult(null);
      setCsvContent('');
      setParsedCsv(null);

      try {
        const rawContent = await readFileContent(file);

        if (!isActive) return;

        if (!rawContent.trim()) {
          throw new Error('CSV 檔案內容為空，請檢查檔案內容後重試。');
        }

        const parsed = parseCSV(rawContent);
        setCsvContent(rawContent);
        setParsedCsv(parsed);
      } catch (error) {
        if (!isActive) return;
        setParseError(error instanceof Error ? error.message : '解析 CSV 失敗，請稍後再試。');
      } finally {
        if (isActive) {
          setIsParsing(false);
        }
      }
    };

    void parseFile();

    return () => {
      isActive = false;
    };
  }, [file, isOpen]);

  const handleClose = () => {
    setIsParsing(false);
    setIsPreviewLoading(false);
    setIsExecuting(false);
    setCsvContent('');
    setParsedCsv(null);
    setPreviewResult(null);
    setParseError(null);
    setApiError(null);
    onClose();
  };

  const handlePreview = async () => {
    if (!csvContent.trim()) return;

    setIsPreviewLoading(true);
    setApiError(null);
    setPreviewResult(null);

    try {
      const result = await fetchApi<ImportPreviewResult>('/bank-accounts/import', {
        method: 'POST',
        body: JSON.stringify({
          mode: 'preview',
          csvContent,
        }),
      });

      setPreviewResult(result);
    } catch (error) {
      setApiError(error instanceof Error ? error.message : '預覽失敗，請稍後再試。');
    } finally {
      setIsPreviewLoading(false);
    }
  };

  const handleExecuteImport = async () => {
    if (!previewResult || !csvContent.trim()) return;

    setIsExecuting(true);
    setApiError(null);

    try {
      const result = await fetchApi<ImportExecuteResult>('/bank-accounts/import', {
        method: 'POST',
        body: JSON.stringify({
          mode: 'execute',
          csvContent,
        }),
      });

      await queryClient.invalidateQueries({ queryKey: BANK_ACCOUNTS_QUERY_KEY });
      invalidatePerformanceAndAssetsCaches(queryClient, ASSETS_KEYS.summary());

      toast.success(`匯入完成：新增 ${result.createdCount} 筆、更新 ${result.updatedCount} 筆、略過 ${result.skippedCount} 筆`);
      handleClose();
    } catch (error) {
      const message = error instanceof Error ? error.message : '執行匯入失敗，請稍後再試。';
      setApiError(message);
      toast.error(`匯入失敗：${message}`);
    } finally {
      setIsExecuting(false);
    }
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50">
      <div className="card-dark rounded-xl shadow-xl w-full max-w-3xl max-h-[90vh] overflow-hidden flex flex-col m-4">
        <div className="flex items-center justify-between px-6 py-4 border-b border-[var(--border-color)]">
          <h2 className="text-xl font-semibold text-[var(--text-primary)]">匯入銀行帳戶</h2>
          <button
            type="button"
            onClick={handleClose}
            className="p-2 text-[var(--text-muted)] hover:text-[var(--text-primary)] hover:bg-[var(--bg-hover)] rounded-lg transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        <div className="flex-1 overflow-y-auto p-6 space-y-4">
          <div className="bg-[var(--bg-secondary)] border border-[var(--border-color)] rounded-lg p-4">
            <p className="text-sm text-[var(--text-muted)] mb-1">已選擇檔案</p>
            <div className="flex items-center justify-between gap-2">
              <div className="flex items-center gap-2 text-[var(--text-primary)] min-w-0">
                <FileText className="w-4 h-4 flex-shrink-0" />
                <span className="font-medium truncate">{file.name}</span>
              </div>
              <span className="text-xs text-[var(--text-muted)] whitespace-nowrap">
                {(file.size / 1024).toFixed(1)} KB
              </span>
            </div>
          </div>

          {isParsing && (
            <div className="bg-[var(--bg-secondary)] border border-[var(--border-color)] rounded-lg p-6 flex items-center gap-3 text-[var(--text-secondary)]">
              <Loader2 className="w-5 h-5 animate-spin" />
              <span>解析檔案中...</span>
            </div>
          )}

          {parseError && (
            <div className="bg-[var(--color-danger-soft)] border border-[var(--color-danger)]/40 rounded-lg p-4">
              <div className="flex items-start gap-2">
                <AlertCircle className="w-4 h-4 mt-0.5 text-[var(--color-danger)]" />
                <p className="text-sm text-[var(--color-danger)]">{parseError}</p>
              </div>
            </div>
          )}

          {!isParsing && !parseError && parsedCsv && (
            <div className="bg-[var(--color-success-soft)] border border-[var(--color-success)]/40 rounded-lg p-4 text-sm text-[var(--text-primary)]">
              已解析 <strong>{parsedCsv.rowCount}</strong> 筆資料，共 <strong>{parsedCsv.headers.length}</strong>{' '}
              個欄位。請點擊「預覽」查看新增 / 更新 / 略過結果。
            </div>
          )}

          {apiError && (
            <div className="bg-[var(--color-danger-soft)] border border-[var(--color-danger)]/40 rounded-lg p-4">
              <div className="flex items-start gap-2">
                <AlertCircle className="w-4 h-4 mt-0.5 text-[var(--color-danger)]" />
                <p className="text-sm text-[var(--color-danger)]">{apiError}</p>
              </div>
            </div>
          )}

          {isPreviewLoading && (
            <div className="bg-[var(--bg-secondary)] border border-[var(--border-color)] rounded-lg p-6 flex items-center gap-3 text-[var(--text-secondary)]">
              <Loader2 className="w-5 h-5 animate-spin" />
              <span>產生預覽中...</span>
            </div>
          )}

          {!isPreviewLoading && previewResult && (
            <div className="space-y-4">
              <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                <div className={`rounded-lg px-4 py-3 ${ACTION_META.create.summaryClassName}`}>
                  <p className="text-xs">新增</p>
                  <p className="text-xl font-semibold">{previewSummary.create}</p>
                </div>
                <div className={`rounded-lg px-4 py-3 ${ACTION_META.update.summaryClassName}`}>
                  <p className="text-xs">更新</p>
                  <p className="text-xl font-semibold">{previewSummary.update}</p>
                </div>
                <div className={`rounded-lg px-4 py-3 ${ACTION_META.skip.summaryClassName}`}>
                  <p className="text-xs">略過</p>
                  <p className="text-xl font-semibold">{previewSummary.skip}</p>
                </div>
              </div>

              {previewResult.validationErrors.length > 0 && (
                <div className="bg-[var(--color-danger-soft)] border border-[var(--color-danger)]/40 rounded-lg p-4">
                  <div className="flex items-center gap-2 mb-2">
                    <AlertCircle className="w-4 h-4 text-[var(--color-danger)]" />
                    <p className="text-sm font-medium text-[var(--color-danger)]">驗證錯誤</p>
                  </div>
                  <ul className="list-disc ml-5 space-y-1 text-sm text-[var(--color-danger)]">
                    {previewResult.validationErrors.map((error, index) => (
                      <li key={`${error}-${index}`}>{error}</li>
                    ))}
                  </ul>
                </div>
              )}

              <div className="border border-[var(--border-color)] rounded-lg overflow-hidden">
                <div className="overflow-x-auto">
                  <table className="w-full text-sm min-w-[720px]">
                    <thead className="bg-[var(--bg-secondary)] border-b border-[var(--border-color)]">
                      <tr>
                        <th className="px-4 py-3 text-left text-[var(--text-muted)] font-medium">銀行名稱</th>
                        <th className="px-4 py-3 text-left text-[var(--text-muted)] font-medium">動作</th>
                        <th className="px-4 py-3 text-left text-[var(--text-muted)] font-medium">變更內容</th>
                      </tr>
                    </thead>
                    <tbody>
                      {previewResult.items.length === 0 && (
                        <tr>
                          <td colSpan={3} className="px-4 py-6 text-center text-[var(--text-muted)]">
                            無可預覽資料
                          </td>
                        </tr>
                      )}

                      {previewResult.items.map((item, index) => {
                        const action = normalizeAction(item.action);

                        return (
                          <tr
                            key={`${item.bankName}-${index}`}
                            className="border-b border-[var(--border-color)] last:border-b-0"
                          >
                            <td className="px-4 py-3 text-[var(--text-primary)]">{item.bankName}</td>
                            <td className="px-4 py-3">
                              <span className={`inline-flex items-center rounded-full px-2.5 py-1 text-xs font-medium ${ACTION_META[action].badgeClassName}`}>
                                {ACTION_META[action].label}
                              </span>
                            </td>
                            <td className="px-4 py-3 text-[var(--text-secondary)]">
                              {item.changeDetails.length > 0 ? (
                                <ul className="list-disc ml-5 space-y-1">
                                  {item.changeDetails.map((detail, detailIndex) => (
                                    <li key={`${item.bankName}-${detailIndex}`}>{detail}</li>
                                  ))}
                                </ul>
                              ) : (
                                <span className="text-[var(--text-muted)]">
                                  {action === 'create' ? '將建立新帳戶' : '無變更或已略過'}
                                </span>
                              )}
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
          )}

          {!isParsing && !parseError && !isPreviewLoading && !previewResult && (
            <div className="bg-[var(--bg-secondary)] border border-dashed border-[var(--border-color)] rounded-lg p-6 text-center text-sm text-[var(--text-muted)]">
              點擊「預覽」以檢視匯入結果。
            </div>
          )}
        </div>

        <div className="px-6 py-4 border-t border-[var(--border-color)] flex justify-end gap-2">
          <button
            type="button"
            onClick={handleClose}
            disabled={isPreviewLoading || isExecuting}
            className="btn-dark px-4 py-2 disabled:opacity-50"
          >
            取消
          </button>
          <button
            type="button"
            onClick={handlePreview}
            disabled={isParsing || !csvContent || isPreviewLoading || isExecuting}
            className="btn-dark px-4 py-2 disabled:opacity-50 inline-flex items-center gap-2"
          >
            {isPreviewLoading && <Loader2 className="w-4 h-4 animate-spin" />}
            {isPreviewLoading ? '預覽中...' : '預覽'}
          </button>
          <button
            type="button"
            onClick={handleExecuteImport}
            disabled={isParsing || !previewResult || isPreviewLoading || isExecuting}
            className="btn-accent px-4 py-2 disabled:opacity-50 inline-flex items-center gap-2"
          >
            {isExecuting && <Loader2 className="w-4 h-4 animate-spin" />}
            {isExecuting ? '匯入中...' : '確認匯入'}
          </button>
        </div>
      </div>
    </div>
  );
}
