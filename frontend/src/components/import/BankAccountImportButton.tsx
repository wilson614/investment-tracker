/**
 * BankAccountImportButton
 *
 * 銀行帳戶 CSV 匯入按鈕：點擊按鈕開啟檔案選擇器，並將選擇的檔案交由外層處理。
 */
import { useRef } from 'react';
import { Upload, Loader2 } from 'lucide-react';

interface BankAccountImportButtonProps {
  /** 使用者選擇檔案後回傳給父層 */
  onFileSelected: (file: File) => void;
  /** 載入狀態 */
  isLoading?: boolean;
  /** 縮小版按鈕樣式 */
  compact?: boolean;
  /** 若提供，改用自訂 trigger（常用於搭配 FileDropdown） */
  renderTrigger?: (onClick: () => void) => React.ReactNode;
}

export function BankAccountImportButton({
  onFileSelected,
  isLoading = false,
  compact = false,
  renderTrigger,
}: BankAccountImportButtonProps) {
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleOpenImport = () => {
    if (isLoading) return;
    fileInputRef.current?.click();
  };

  const handleFileChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (!file) return;

    onFileSelected(file);

    // Reset input value to allow selecting the same file again
    event.target.value = '';
  };

  return (
    <>
      <input
        ref={fileInputRef}
        type="file"
        accept=".csv,text/csv"
        onChange={handleFileChange}
        className="hidden"
      />

      {renderTrigger ? (
        renderTrigger(isLoading ? () => undefined : handleOpenImport)
      ) : (
        <button
          type="button"
          onClick={handleOpenImport}
          disabled={isLoading}
          className={`${compact
            ? 'btn-dark flex items-center gap-2 px-3 py-1.5 text-sm'
            : 'btn-dark flex items-center gap-2'
          } ${isLoading ? 'opacity-60 cursor-not-allowed' : ''}`}
        >
          {isLoading ? (
            <Loader2 className={compact ? 'w-3.5 h-3.5 animate-spin' : 'w-4 h-4 animate-spin'} />
          ) : (
            <Upload className={compact ? 'w-3.5 h-3.5' : 'w-4 h-4'} />
          )}
          {isLoading ? '匯入中...' : '匯入'}
        </button>
      )}
    </>
  );
}
