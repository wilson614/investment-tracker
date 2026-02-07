import { useMemo } from 'react';
import { PieChart, Pie, Cell, ResponsiveContainer, Tooltip, Legend } from 'recharts';
import { Skeleton } from '../../../components/common/SkeletonLoader';
import { formatCurrency } from '../../../utils/currency';

interface AssetsBreakdownPieChartProps {
  portfolioMarketValue: number;
  cashBalance: number;
  disposableDeposit: number;
  nonDisposableDeposit: number;
  isLoading: boolean;
}

interface ChartSlice {
  key: 'portfolioMarketValue' | 'cashBalance' | 'disposableDeposit' | 'nonDisposableDeposit';
  name: string;
  value: number;
  color: string;
  [key: string]: string | number;
}

export function AssetsBreakdownPieChart({
  portfolioMarketValue,
  cashBalance,
  disposableDeposit,
  nonDisposableDeposit,
  isLoading,
}: AssetsBreakdownPieChartProps) {
  const chartData = useMemo<ChartSlice[]>(
    () => [
      {
        key: 'portfolioMarketValue',
        name: '股票市值',
        value: portfolioMarketValue,
        color: '#3b82f6',
      },
      {
        key: 'cashBalance',
        name: '帳本現金',
        value: cashBalance,
        color: '#22c55e',
      },
      {
        key: 'disposableDeposit',
        name: '可動用存款',
        value: disposableDeposit,
        color: '#eab308',
      },
      {
        key: 'nonDisposableDeposit',
        name: '不可動用存款',
        value: nonDisposableDeposit,
        color: '#9ca3af',
      },
    ],
    [portfolioMarketValue, cashBalance, disposableDeposit, nonDisposableDeposit]
  );

  const total = useMemo(() => chartData.reduce((sum, item) => sum + item.value, 0), [chartData]);

  if (isLoading) {
    return (
      <div className="card-dark p-6 h-[400px] flex flex-col items-center justify-center">
        <Skeleton width="w-48" height="h-48" circle />
        <div className="mt-4 flex gap-4">
          <Skeleton width="w-20" height="h-4" />
          <Skeleton width="w-20" height="h-4" />
          <Skeleton width="w-20" height="h-4" />
          <Skeleton width="w-20" height="h-4" />
        </div>
      </div>
    );
  }

  if (total <= 0) {
    return (
      <div className="card-dark p-6 h-[400px] flex flex-col items-center justify-center text-[var(--text-muted)]">
        <p>尚無資產資料</p>
      </div>
    );
  }

  const formatTooltip = (value: number | string | Array<number | string> | undefined) => {
    if (typeof value === 'number') {
      return formatCurrency(value, 'TWD');
    }

    return value;
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
              paddingAngle={4}
              dataKey="value"
              nameKey="name"
              stroke="none"
            >
              {chartData.map((item) => (
                <Cell key={item.key} fill={item.color} />
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
