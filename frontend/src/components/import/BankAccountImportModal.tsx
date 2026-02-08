import { useEffect, useState } from 'react';
import { X, FileText, Loader2 } from 'lucide-react';

interface BankAccountImportModalProps {
  isOpen: boolean;
  onClose: () => void;
  file: File;
}

export function BankAccountImportModal({ isOpen, onClose, file }: BankAccountImportModalProps) {
  const [isParsing, setIsParsing] = useState(false);

  useEffect(() => {
    if (!isOpen) return;

    let active = true;
    setIsParsing(true);

    const timer = window.setTimeout(() => {
      if (!active) return;
      setIsParsing(false);
    }, 350);

    return () => {
      active = false;
      window.clearTimeout(timer);
    };
  }, [isOpen, file]);

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50">
      <div className="card-dark rounded-xl shadow-xl w-full max-w-3xl max-h-[90vh] overflow-hidden flex flex-col m-4">
        <div className="flex items-center justify-between px-6 py-4 border-b border-[var(--border-color)]">
          <h2 className="text-xl font-semibold text-[var(--text-primary)]">匯入銀行帳戶</h2>
          <button
            type="button"
            onClick={onClose}
            className="p-2 text-[var(--text-muted)] hover:text-[var(--text-primary)] hover:bg-[var(--bg-hover)] rounded-lg transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        <div className="flex-1 overflow-y-auto p-6 space-y-6">
          <div className="bg-[var(--bg-secondary)] border border-[var(--border-color)] rounded-lg p-4">
            <p className="text-sm text-[var(--text-muted)] mb-1">已選擇檔案</p>
            <div className="flex items-center gap-2 text-[var(--text-primary)]">
              <FileText className="w-4 h-4" />
              <span className="font-medium break-all">{file.name}</span>
            </div>
          </div>

          {isParsing ? (
            <div className="bg-[var(--bg-secondary)] border border-[var(--border-color)] rounded-lg p-6 flex items-center gap-3 text-[var(--text-secondary)]">
              <Loader2 className="w-5 h-5 animate-spin" />
              <span>解析檔案中...</span>
            </div>
          ) : (
            <div className="bg-[var(--bg-secondary)] border border-[var(--border-color)] rounded-lg p-6">
              <h3 className="text-lg font-medium text-[var(--text-primary)] mb-2">預覽</h3>
              <p className="text-[var(--text-muted)] text-sm mb-4">
                預覽內容將於後續批次完成。此區塊已保留結構供後續串接。
              </p>
              <div className="border border-dashed border-[var(--border-color)] rounded-lg p-6 text-center text-[var(--text-muted)] text-sm">
                尚無預覽資料
              </div>
            </div>
          )}
        </div>

        <div className="px-6 py-4 border-t border-[var(--border-color)] flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            className="btn-dark px-4 py-2"
          >
            Cancel
          </button>
          <button
            type="button"
            disabled={isParsing}
            className="btn-accent px-4 py-2 disabled:opacity-50"
          >
            Preview
          </button>
        </div>
      </div>
    </div>
  );
}
