import { PieChart, Pie, Cell, ResponsiveContainer, Legend, Tooltip } from 'recharts';
import type { PieLabelRenderProps } from 'recharts';
import type { StockPosition } from '../../types';

interface PortfolioDistributionChartProps {
  positions: StockPosition[];
  homeCurrency?: string;
}

const COLORS = [
  '#e8a87c', // peach
  '#e8d98c', // butter
  '#d4a5a5', // blush
  '#e8d5b5', // cream
  '#c4956a', // caramel
  '#9abe8c', // success green
  '#dcc68a', // warning
  '#d98a8a', // danger
];

export function PortfolioDistributionChart({
  positions,
  homeCurrency = 'TWD',
}: PortfolioDistributionChartProps) {
  if (positions.length === 0) {
    return (
      <div className="flex items-center justify-center h-64 text-[var(--text-muted)]">
        尚無持倉資料
      </div>
    );
  }

  const data = positions.map((pos, index) => ({
    name: pos.ticker,
    value: pos.totalCostHome,
    color: COLORS[index % COLORS.length],
  }));

  const totalValue = data.reduce((sum, item) => sum + item.value, 0);

  const formatNumber = (value: number) => {
    return value.toLocaleString('zh-TW', {
      minimumFractionDigits: 0,
      maximumFractionDigits: 0,
    });
  };

  const renderCustomLabel = (props: PieLabelRenderProps) => {
    const { cx, cy, midAngle, innerRadius, outerRadius, percent } = props;
    if (
      typeof cx !== 'number' ||
      typeof cy !== 'number' ||
      typeof midAngle !== 'number' ||
      typeof innerRadius !== 'number' ||
      typeof outerRadius !== 'number' ||
      typeof percent !== 'number' ||
      percent < 0.05
    ) {
      return null;
    }
    const RADIAN = Math.PI / 180;
    const radius = innerRadius + (outerRadius - innerRadius) * 0.5;
    const x = cx + radius * Math.cos(-midAngle * RADIAN);
    const y = cy + radius * Math.sin(-midAngle * RADIAN);

    return (
      <text
        x={x}
        y={y}
        fill="white"
        textAnchor="middle"
        dominantBaseline="central"
        fontSize={12}
        fontWeight={600}
      >
        {`${(percent * 100).toFixed(0)}%`}
      </text>
    );
  };

  return (
    <div className="card-dark p-6">
      <h3 className="text-lg font-semibold text-[var(--text-primary)] mb-4">資產分佈</h3>
      <div className="h-64">
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Pie
              data={data}
              cx="50%"
              cy="50%"
              labelLine={false}
              label={renderCustomLabel}
              outerRadius={100}
              innerRadius={50}
              dataKey="value"
              stroke="none"
            >
              {data.map((entry, index) => (
                <Cell key={`cell-${index}`} fill={entry.color} />
              ))}
            </Pie>
            <Tooltip
              formatter={(value, name) => {
                const numValue = typeof value === 'number' ? value : 0;
                const displayName = typeof name === 'string' ? name : '';
                return [
                  `${formatNumber(numValue)} ${homeCurrency} (${((numValue / totalValue) * 100).toFixed(1)}%)`,
                  displayName,
                ];
              }}
              contentStyle={{
                borderRadius: '8px',
                border: '1px solid var(--border-color)',
                backgroundColor: 'var(--bg-card)',
                boxShadow: '0 4px 6px -1px rgb(0 0 0 / 0.3)',
              }}
              itemStyle={{
                color: 'var(--text-primary)',
              }}
            />
            <Legend
              layout="vertical"
              align="right"
              verticalAlign="middle"
              formatter={(value) => (
                <span className="text-sm text-[var(--text-secondary)]">{value}</span>
              )}
            />
          </PieChart>
        </ResponsiveContainer>
      </div>
      <div className="mt-4 pt-4 border-t border-[var(--border-color)]">
        <div className="flex justify-between text-sm">
          <span className="text-[var(--text-muted)]">總成本</span>
          <span className="font-semibold text-[var(--text-primary)]">
            {formatNumber(totalValue)} {homeCurrency}
          </span>
        </div>
      </div>
    </div>
  );
}
