/**
 * Toast
 *
 * 全站通知系統 (Toast)：透過 React Context 提供 `success/error/warning/info` API。
 *
 * 行為重點：
 * - `addToast` 會建立唯一 id，並在 duration > 0 時自動移除。
 * - `useToast` 必須在 `ToastProvider` 之內使用。
 */
import { createContext, useContext, useState, useCallback, type ReactNode } from 'react';

export type ToastType = 'success' | 'error' | 'warning' | 'info';

interface Toast {
  /** 唯一識別碼 */
  id: string;
  /** 通知類型 */
  type: ToastType;
  /** 顯示訊息 */
  message: string;
  /** 自動關閉時間 (ms)；<= 0 表示不自動關閉 */
  duration?: number;
}

interface ToastContextType {
  /** 目前顯示中的 toast 列表 */
  toasts: Toast[];
  /** 新增 toast */
  addToast: (type: ToastType, message: string, duration?: number) => void;
  /** 移除指定 toast */
  removeToast: (id: string) => void;
  /** success shorthand */
  success: (message: string, duration?: number) => void;
  /** error shorthand */
  error: (message: string, duration?: number) => void;
  /** warning shorthand */
  warning: (message: string, duration?: number) => void;
  /** info shorthand */
  info: (message: string, duration?: number) => void;
}

const ToastContext = createContext<ToastContextType | undefined>(undefined);

/**
 * ToastProvider
 *
 * 將 toast context 掛到 app tree，並負責渲染 ToastContainer。
 */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);

  const removeToast = useCallback((id: string) => {
    setToasts((prev) => prev.filter((toast) => toast.id !== id));
  }, []);

  const addToast = useCallback(
    (type: ToastType, message: string, duration = 5000) => {
      const id = `toast-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
      const toast: Toast = { id, type, message, duration };

      setToasts((prev) => [...prev, toast]);

      if (duration > 0) {
        setTimeout(() => {
          removeToast(id);
        }, duration);
      }
    },
    [removeToast]
  );

  const success = useCallback(
    (message: string, duration?: number) => addToast('success', message, duration),
    [addToast]
  );

  const error = useCallback(
    (message: string, duration?: number) => addToast('error', message, duration),
    [addToast]
  );

  const warning = useCallback(
    (message: string, duration?: number) => addToast('warning', message, duration),
    [addToast]
  );

  const info = useCallback(
    (message: string, duration?: number) => addToast('info', message, duration),
    [addToast]
  );

  return (
    <ToastContext.Provider
      value={{ toasts, addToast, removeToast, success, error, warning, info }}
    >
      {children}
      <ToastContainer toasts={toasts} removeToast={removeToast} />
    </ToastContext.Provider>
  );
}

/**
 * useToast
 *
 * 取得 toast context；若未包在 ToastProvider 內會丟錯。
 */
export function useToast() {
  const context = useContext(ToastContext);
  if (!context) {
    throw new Error('useToast must be used within a ToastProvider');
  }
  return context;
}

interface ToastContainerProps {
  toasts: Toast[];
  removeToast: (id: string) => void;
}

function ToastContainer({ toasts, removeToast }: ToastContainerProps) {
  return (
    <div className="fixed bottom-4 right-4 z-50 flex flex-col gap-2 max-w-sm">
      {toasts.map((toast) => (
        <ToastItem key={toast.id} toast={toast} onClose={() => removeToast(toast.id)} />
      ))}
    </div>
  );
}

interface ToastItemProps {
  toast: Toast;
  onClose: () => void;
}

const toastStyles: Record<ToastType, { bg: string; icon: string; iconColor: string }> = {
  success: {
    bg: 'bg-[var(--color-success-soft)] border-[var(--color-success)]',
    icon: 'M5 13l4 4L19 7',
    iconColor: 'text-[var(--color-success)]',
  },
  error: {
    bg: 'bg-[var(--color-danger-soft)] border-[var(--color-danger)]',
    icon: 'M6 18L18 6M6 6l12 12',
    iconColor: 'text-[var(--color-danger)]',
  },
  warning: {
    bg: 'bg-[var(--color-warning-soft)] border-[var(--color-warning)]',
    icon: 'M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z',
    iconColor: 'text-[var(--color-warning)]',
  },
  info: {
    bg: 'bg-[var(--accent-butter-soft)] border-[var(--accent-butter)]',
    icon: 'M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z',
    iconColor: 'text-[var(--accent-butter)]',
  },
};

function ToastItem({ toast, onClose }: ToastItemProps) {
  const style = toastStyles[toast.type];

  return (
    <div
      className={`flex items-start gap-3 p-4 rounded-lg border shadow-lg animate-slide-in ${style.bg}`}
      role="alert"
    >
      <svg
        className={`w-5 h-5 flex-shrink-0 ${style.iconColor}`}
        fill="none"
        viewBox="0 0 24 24"
        stroke="currentColor"
      >
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d={style.icon} />
      </svg>
      <p className="text-sm text-[var(--text-primary)] flex-1">{toast.message}</p>
      <button
        onClick={onClose}
        className="p-1 hover:bg-[var(--bg-hover)] rounded transition-colors"
        aria-label="關閉通知"
      >
        <svg className="w-4 h-4 text-[var(--text-muted)]" fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
        </svg>
      </button>
    </div>
  );
}

export default ToastProvider;
