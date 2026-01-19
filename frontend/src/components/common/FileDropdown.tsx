/**
 * FileDropdown
 *
 * 共用「檔案」下拉選單：可同時提供匯入/匯出入口。
 *
 * 行為：
 * - 若只提供匯出（沒有匯入），會退化成單一匯出按鈕。
 * - 打開後點擊外部會自動關閉。
 */
import { useState, useRef, useEffect } from 'react';
import { FileText, ChevronDown } from 'lucide-react';

interface FileDropdownProps {
  /** 點擊匯入 */
  onImport?: () => void;
  /** 點擊匯出 */
  onExport?: () => void;
  /** 禁用匯入 */
  importDisabled?: boolean;
  /** 禁用匯出 */
  exportDisabled?: boolean;
  /** 匯入顯示文字 */
  importLabel?: string;
  /** 匯出顯示文字 */
  exportLabel?: string;
}

export function FileDropdown({
  onImport,
  onExport,
  importDisabled = false,
  exportDisabled = false,
  importLabel = '匯入',
  exportLabel = '匯出',
}: FileDropdownProps) {
  const [isOpen, setIsOpen] = useState(false);
  const dropdownRef = useRef<HTMLDivElement>(null);

  // 點擊 dropdown 外部時自動關閉。
  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) {
        setIsOpen(false);
      }
    };

    if (isOpen) {
      document.addEventListener('mousedown', handleClickOutside);
    }
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [isOpen]);

  /**
   * 點擊匯入：先關閉 dropdown，再觸發 callback。
   */
  const handleImport = () => {
    setIsOpen(false);
    onImport?.();
  };

  /**
   * 點擊匯出：先關閉 dropdown，再觸發 callback。
   */
  const handleExport = () => {
    setIsOpen(false);
    onExport?.();
  };

  const hasImport = !!onImport;
  const hasExport = !!onExport;

  // If only export exists and no import, just show a simple export button
  if (hasExport && !hasImport) {
    return (
      <button
        onClick={onExport}
        disabled={exportDisabled}
        className="btn-dark flex items-center gap-1.5 px-3 py-1.5 text-sm disabled:opacity-50"
        title={exportLabel}
      >
        <FileText className="w-4 h-4" />
        {exportLabel}
      </button>
    );
  }

  return (
    <div className="relative" ref={dropdownRef}>
      <button
        onClick={() => setIsOpen(!isOpen)}
        className="btn-dark flex items-center gap-1.5 px-3 py-1.5 text-sm"
      >
        <FileText className="w-4 h-4" />
        檔案
        <ChevronDown className={`w-3 h-3 transition-transform ${isOpen ? 'rotate-180' : ''}`} />
      </button>

      {isOpen && (
        <div className="absolute right-0 mt-1 py-1 w-28 bg-[var(--bg-secondary)] border border-[var(--border-color)] rounded-lg shadow-lg z-20">
          {hasImport && (
            <button
              onClick={handleImport}
              disabled={importDisabled}
              className="w-full px-4 py-2 text-left text-sm text-[var(--text-primary)] hover:bg-[var(--bg-hover)] disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {importLabel}
            </button>
          )}
          {hasExport && (
            <button
              onClick={handleExport}
              disabled={exportDisabled}
              className="w-full px-4 py-2 text-left text-sm text-[var(--text-primary)] hover:bg-[var(--bg-hover)] disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {exportLabel}
            </button>
          )}
        </div>
      )}
    </div>
  );
}
