import { useMemo } from 'react';

interface PerformanceData {
  label: string;
  value: number;
  tooltip?: string;
}

interface PerformanceBarChartProps {
  data: PerformanceData[];
  title?: string;
  height?: number;
  showValues?: boolean;
  className?: string;
}

export function PerformanceBarChart({
  data,
  title,
  height = 200,
  showValues = true,
  className = '',
}: PerformanceBarChartProps) {
  const { normalizedData } = useMemo(() => {
    const values = data.map(d => d.value);
    const max = Math.max(...values.map(Math.abs), 0.01); // Avoid division by zero
    
    return {
      normalizedData: data.map(d => ({
        ...d,
        percentage: (d.value / max) * 100,
      })),
    };
  }, [data]);

  const formatValue = (value: number) => {
    const sign = value >= 0 ? '+' : '';
    return `${sign}${value.toFixed(2)}%`;
  };

  if (data.length === 0) {
    return (
      <div className={`flex items-center justify-center h-[${height}px] text-[var(--text-muted)] ${className}`}>
        無資料
      </div>
    );
  }

  return (
    <div className={className}>
      {title && (
        <h4 className="text-sm font-medium text-[var(--text-secondary)] mb-3">{title}</h4>
      )}
      
      <div className="space-y-2" style={{ minHeight: height }}>
        {normalizedData.map((item) => {
          const isPositive = item.value >= 0;
          const barWidth = Math.abs(item.percentage);
          
          return (
            <div
              key={item.label}
              className="group relative"
              title={item.tooltip || `${item.label}: ${formatValue(item.value)}`}
            >
              {/* Label and Value Row */}
              <div className="flex justify-between items-center mb-1">
                <span className="text-sm text-[var(--text-secondary)]">{item.label}</span>
                {showValues && (
                  <span className={`text-sm font-medium number-display ${
                    isPositive ? 'number-positive' : 'number-negative'
                  }`}>
                    {formatValue(item.value)}
                  </span>
                )}
              </div>
              
              {/* Bar Container */}
              <div className="relative h-6 bg-[var(--bg-tertiary)] rounded overflow-hidden">
                {/* Center line for bidirectional chart */}
                <div className="absolute left-1/2 top-0 bottom-0 w-px bg-[var(--border-color)]" />
                
                {/* Bar */}
                <div
                  className={`absolute top-0 bottom-0 transition-all duration-300 ${
                    isPositive
                      ? 'left-1/2 bg-[var(--color-success)]'
                      : 'right-1/2 bg-[var(--color-danger)]'
                  }`}
                  style={{
                    width: `${barWidth / 2}%`,
                  }}
                />
                
                {/* Hover highlight */}
                <div className="absolute inset-0 bg-white/0 group-hover:bg-white/5 transition-colors" />
              </div>
              
              {/* Tooltip on hover */}
              <div className="absolute left-1/2 -translate-x-1/2 -top-10 opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none z-10">
                <div className="bg-[var(--bg-primary)] border border-[var(--border-color)] rounded px-2 py-1 text-xs shadow-lg whitespace-nowrap">
                  {item.tooltip || `${item.label}: ${formatValue(item.value)}`}
                </div>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

export default PerformanceBarChart;
