/**
 * ErrorDisplay
 *
 * 共用錯誤顯示元件：可做 full page 或 inline card 顯示，並可選擇提供重試按鈕。
 */
interface ErrorDisplayProps {
  /** 標題（預設：發生錯誤） */
  title?: string;
  /** 錯誤訊息 */
  message: string;
  /** 重試 callback （若提供則顯示按鈕） */
  onRetry?: () => void;
  /** 是否以較大的 full page 版型顯示 */
  fullPage?: boolean;
}

export function ErrorDisplay({
  title = '發生錯誤',
  message,
  onRetry,
  fullPage = false,
}: ErrorDisplayProps) {
  const content = (
    <div className="text-center">
      <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-red-100 mb-4">
        <svg
          className="w-8 h-8 text-red-600"
          fill="none"
          viewBox="0 0 24 24"
          stroke="currentColor"
        >
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            strokeWidth={2}
            d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z"
          />
        </svg>
      </div>
      <h3 className="text-lg font-semibold text-gray-900 mb-2">{title}</h3>
      <p className="text-gray-600 mb-4">{message}</p>
      {onRetry && (
        <button
          onClick={onRetry}
          className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors"
        >
          重試
        </button>
      )}
    </div>
  );

  if (fullPage) {
    return (
      <div className="flex items-center justify-center min-h-[50vh] p-4">
        {content}
      </div>
    );
  }

  return (
    <div className="bg-red-50 border border-red-200 rounded-lg p-6">
      {content}
    </div>
  );
}

/**
 * InlineError
 *
 * 較輕量的行內錯誤提示，可選擇提供關閉按鈕。
 */
interface InlineErrorProps {
  /** 錯誤訊息 */
  message: string;
  /** 關閉 callback （若提供則顯示關閉按鈕） */
  onDismiss?: () => void;
}

export function InlineError({ message, onDismiss }: InlineErrorProps) {
  return (
    <div className="flex items-center gap-2 p-3 bg-red-50 border border-red-200 rounded-lg text-red-700">
      <svg
        className="w-5 h-5 flex-shrink-0"
        fill="none"
        viewBox="0 0 24 24"
        stroke="currentColor"
      >
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          strokeWidth={2}
          d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
        />
      </svg>
      <span className="text-sm flex-1">{message}</span>
      {onDismiss && (
        <button
          onClick={onDismiss}
          className="p-1 hover:bg-red-100 rounded transition-colors"
        >
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
          </svg>
        </button>
      )}
    </div>
  );
}

export default ErrorDisplay;
