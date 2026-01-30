import { useMemo } from 'react';
import { PieChart, Pie, Cell, ResponsiveContainer, Tooltip, Legend } from 'recharts';
import { Skeleton } from '../../../components/common/SkeletonLoader';
import type { TotalAssetsSummary } from '../types';

interface AssetsBreakdownPieChartProps {
  data?: TotalAssetsSummary;
  isLoading: boolean;
}

const COLORS = ['var(--accent-peach)', '#60a5fa']; // investment (peach) and bank (blue-400)

export function AssetsBreakdownPieChart({ data, isLoading }: AssetsBreakdownPieChartProps) {
  const chartData = useMemo(() => {
    if (!data) return [];
    return [
      { name: '投資部位', value: data.investmentTotal },
      { name: '銀行存款', value: data.bankTotal },
    ].filter(item => item.value > 0);
  }, [data]);

  if (isLoading) {
    return (
      <div className="card-dark p-6 h-[400px] flex flex-col items-center justify-center">
        <Skeleton width="w-48" height="h-48" circle />
        <div className="mt-4 flex gap-4">
          <Skeleton width="w-20" height="h-4" />
          <Skeleton width="w-20" height="h-4" />
        </div>
      </div>
    );
  }

  // If no data or zero total, show empty state
  if (chartData.length === 0) {
    return (
      <div className="card-dark p-6 h-[400px] flex flex-col items-center justify-center text-[var(--text-muted)]">
        <p>尚無資產資料</p>
      </div>
    );
  }

  const formatTooltip = (value: number | string | Array<number | string> | undefined) => {
    if (typeof value === 'number') {
      return [`NT$ ${Math.round(value).toLocaleString('zh-TW')}`];
    }
    return [value];
  };

  return (
    <div className="card-dark p-6 h-[400px]">
      <h3 className="text-[var(--text-secondary)] font-medium mb-4">資產配置分析</h3>
      <div className="w-full h-[320px]">
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Pie
              data={chartData}
              cx="50%"
              cy="50%"
              innerRadius={60}
              outerRadius={100}
              paddingAngle={5}
              dataKey="value"
              stroke="none"
            >
              {chartData.map((_, index) => (
                <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
              ))}
            </Pie>
            <Tooltip
              formatter={formatTooltip}
              contentStyle={{
                backgroundColor: 'var(--bg-card)',
                borderColor: 'var(--border-color)',
                borderRadius: '0.5rem',
                color: 'var(--text-primary)',
              }}
              itemStyle={{ color: 'var(--text-primary)' }}
            />
            <Legend
              verticalAlign="bottom"
              height={36}
              formatter={(value) => <span style={{ color: 'var(--text-secondary)' }}>{value}</span>}
            />
          </PieChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}
