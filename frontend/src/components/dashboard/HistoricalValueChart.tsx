/**
 * HistoricalValueChart
 *
 * 折線圖：顯示投資組合歷年淨值變化。
 * X 軸為年份，Y 軸為期末價值（home currency）。
 */
import { useMemo } from 'react';
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  ReferenceLine,
} from 'recharts';

interface MonthValue {
  month: string;
  value: number | null;
  contributions: number | null;
}

interface HistoricalValueChartProps {
  data: MonthValue[];
  currency: string;
  height?: number;
  className?: string;
}

export function HistoricalValueChart({
  data,
  currency,
  height = 250,
  className = '',
}: HistoricalValueChartProps) {
  const chartData = useMemo(() => {
    return data
      .filter((d) => d.value != null)
      .map((d) => ({
        month: d.month,
        value: d.value,
        contributions: d.contributions,
      }));
  }, [data]);

  const formatValue = (value: number) => {
    if (value >= 1000000) {
      return `${(value / 1000000).toFixed(1)}M`;
    }
    if (value >= 1000) {
      return `${(value / 1000).toFixed(0)}K`;
    }
    return value.toFixed(0);
  };

  const formatFullValue = (value: number) => {
    return Math.round(value).toLocaleString('zh-TW');
  };

  if (chartData.length === 0) {
    return (
      <div className={`flex items-center justify-center text-[var(--text-muted)] ${className}`} style={{ height }}>
        無歷史資料
      </div>
    );
  }

  // Only show chart if there are at least 2 data points
  if (chartData.length < 2) {
    return (
      <div className={`flex items-center justify-center text-[var(--text-muted)] ${className}`} style={{ height }}>
        需要至少兩個月份資料才能顯示圖表
      </div>
    );
  }

  return (
    <div className={className} style={{ height }}>
      <ResponsiveContainer width="100%" height="100%">
        <LineChart
          data={chartData}
          margin={{ top: 10, right: 10, left: 10, bottom: 0 }}
        >
          <CartesianGrid strokeDasharray="3 3" stroke="var(--border-color)" />
          <XAxis
            dataKey="month"
            stroke="var(--text-muted)"
            tick={{ fill: 'var(--text-muted)', fontSize: 12 }}
            axisLine={{ stroke: 'var(--border-color)' }}
            interval={Math.floor(chartData.length / 12)}
          />
          <YAxis
            tickFormatter={formatValue}
            stroke="var(--text-muted)"
            tick={{ fill: 'var(--text-muted)', fontSize: 12 }}
            axisLine={{ stroke: 'var(--border-color)' }}
            width={60}
          />
          <Tooltip
            contentStyle={{
              backgroundColor: 'var(--bg-secondary)',
              border: '1px solid var(--border-color)',
              borderRadius: '8px',
            }}
            labelStyle={{ color: 'var(--text-primary)' }}
            formatter={(value, name) => {
              if (value == null) return ['—', name];
              const numValue = typeof value === 'number' ? value : 0;
              if (name === 'value') {
                return [`${formatFullValue(numValue)} ${currency}`, '期末價值'];
              }
              if (name === 'contributions') {
                return [`${formatFullValue(numValue)} ${currency}`, '累計投入'];
              }
              return [value, name];
            }}
          />
          <ReferenceLine y={0} stroke="var(--border-color)" />
          {/* Contributions line (dashed) */}
          <Line
            type="monotone"
            dataKey="contributions"
            stroke="var(--text-muted)"
            strokeWidth={1}
            strokeDasharray="5 5"
            dot={false}
            activeDot={{ r: 4, fill: 'var(--text-muted)' }}
          />
          {/* Value line */}
          <Line
            type="monotone"
            dataKey="value"
            stroke="var(--accent-peach)"
            strokeWidth={2}
            dot={{ r: 4, fill: 'var(--accent-peach)', strokeWidth: 0 }}
            activeDot={{ r: 6, fill: 'var(--accent-peach)', strokeWidth: 2, stroke: 'var(--bg-primary)' }}
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}

export default HistoricalValueChart;
