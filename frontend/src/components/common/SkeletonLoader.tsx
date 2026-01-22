/**
 * SkeletonLoader
 *
 * 骨架屏載入元件：在資料載入時顯示灰色動畫區塊，避免版面跳動。
 */

interface SkeletonProps {
  /** 自訂寬度（CSS class 或 style） */
  width?: string;
  /** 自訂高度（CSS class 或 style） */
  height?: string;
  /** 是否為圓形 */
  circle?: boolean;
  /** 額外 className */
  className?: string;
}

/**
 * 基礎骨架元素：支援自訂尺寸與形狀。
 */
export function Skeleton({
  width = 'w-full',
  height = 'h-4',
  circle = false,
  className = '',
}: SkeletonProps) {
  return (
    <div
      className={`
        animate-pulse bg-[var(--bg-tertiary)]
        ${circle ? 'rounded-full' : 'rounded'}
        ${width} ${height}
        ${className}
      `}
    />
  );
}

interface SkeletonTextProps {
  /** 文字行數 */
  lines?: number;
  /** 額外 className */
  className?: string;
}

/**
 * 多行文字骨架：模擬段落載入中的效果。
 */
export function SkeletonText({ lines = 3, className = '' }: SkeletonTextProps) {
  return (
    <div className={`space-y-2 ${className}`}>
      {Array.from({ length: lines }).map((_, i) => (
        <Skeleton
          key={i}
          width={i === lines - 1 ? 'w-3/4' : 'w-full'}
          height="h-4"
        />
      ))}
    </div>
  );
}

interface SkeletonCardProps {
  /** 額外 className */
  className?: string;
}

/**
 * 卡片骨架：模擬 metric card 載入中的效果。
 */
export function SkeletonCard({ className = '' }: SkeletonCardProps) {
  return (
    <div className={`card-dark p-4 ${className}`}>
      <Skeleton width="w-24" height="h-4" className="mb-2" />
      <Skeleton width="w-16" height="h-8" />
    </div>
  );
}

interface SkeletonChartProps {
  /** 圖表高度 */
  height?: string;
  /** 額外 className */
  className?: string;
}

/**
 * 圖表骨架：模擬圖表載入中的效果，包含標題和圖表區域。
 */
export function SkeletonChart({ height = 'h-64', className = '' }: SkeletonChartProps) {
  return (
    <div className={`card-dark p-6 ${className}`}>
      <Skeleton width="w-32" height="h-6" className="mb-4" />
      <Skeleton width="w-full" height={height} />
    </div>
  );
}

interface SkeletonTableProps {
  /** 列數 */
  rows?: number;
  /** 欄數 */
  columns?: number;
  /** 額外 className */
  className?: string;
}

/**
 * 表格骨架：模擬表格載入中的效果。
 */
export function SkeletonTable({
  rows = 5,
  columns = 4,
  className = '',
}: SkeletonTableProps) {
  return (
    <div className={`space-y-2 ${className}`}>
      {/* Header */}
      <div className="flex gap-4 pb-2 border-b border-[var(--border-color)]">
        {Array.from({ length: columns }).map((_, i) => (
          <Skeleton key={i} width="flex-1" height="h-4" />
        ))}
      </div>
      {/* Rows */}
      {Array.from({ length: rows }).map((_, rowIdx) => (
        <div key={rowIdx} className="flex gap-4 py-2">
          {Array.from({ length: columns }).map((_, colIdx) => (
            <Skeleton key={colIdx} width="flex-1" height="h-4" />
          ))}
        </div>
      ))}
    </div>
  );
}

export default Skeleton;
