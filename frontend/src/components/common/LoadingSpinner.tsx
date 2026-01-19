/**
 * LoadingSpinner
 *
 * 共用載入指示元件：支援大小、提示文字，以及 full-screen 覆蓋模式。
 */
interface LoadingSpinnerProps {
  /** spinner 尺寸 */
  size?: 'sm' | 'md' | 'lg';
  /** 顯示在 spinner 下方的文字 */
  text?: string;
  /** 是否以 full-screen overlay 顯示 */
  fullScreen?: boolean;
}

const sizeClasses = {
  sm: 'h-4 w-4 border-2',
  md: 'h-8 w-8 border-2',
  lg: 'h-12 w-12 border-3',
};

export function LoadingSpinner({
  size = 'md',
  text,
  fullScreen = false,
}: LoadingSpinnerProps) {
  const spinner = (
    <div className="flex flex-col items-center justify-center gap-3">
      <div
        className={`animate-spin rounded-full border-[var(--border-color)] border-t-[var(--accent-peach)] ${sizeClasses[size]}`}
      />
      {text && <p className="text-sm text-[var(--text-muted)]">{text}</p>}
    </div>
  );

  if (fullScreen) {
    return (
      <div className="fixed inset-0 flex items-center justify-center bg-[var(--bg-primary)]/90 z-50">
        {spinner}
      </div>
    );
  }

  return spinner;
}

/**
 * 版面型載入：置中顯示較大的 spinner。
 */
export function PageLoader({ text = '載入中...' }: { text?: string }) {
  return (
    <div className="flex items-center justify-center min-h-[50vh]">
      <LoadingSpinner size="lg" text={text} />
    </div>
  );
}

/**
 * 行內載入：用於小區塊內顯示 spinner + 可選文字。
 */
export function InlineLoader({ text }: { text?: string }) {
  return (
    <div className="flex items-center gap-2 text-[var(--text-muted)]">
      <LoadingSpinner size="sm" />
      {text && <span className="text-sm">{text}</span>}
    </div>
  );
}

export default LoadingSpinner;
