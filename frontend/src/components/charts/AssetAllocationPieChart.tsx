import { PieChart, Pie, Cell, ResponsiveContainer, Tooltip, Legend } from 'recharts';
import { getChartColor } from '../../constants/chartColors';

export interface AssetAllocationData {
  ticker: string;
  value: number;
  percentage: number;
  [key: string]: unknown; // Index signature for Recharts compatibility
}

interface AssetAllocationPieChartProps {
  data: AssetAllocationData[];
  homeCurrency?: string;
}

// Custom tooltip component
const CustomTooltip = ({
  active,
  payload,
  homeCurrency = 'TWD',
}: {
  active?: boolean;
  payload?: Array<{ payload: AssetAllocationData }>;
  homeCurrency?: string;
}) => {
  if (!active || !payload || payload.length === 0) return null;

  const item = payload[0].payload;
  return (
    <div className="bg-[var(--bg-secondary)] border border-[var(--border-color)] rounded-lg p-3 shadow-lg">
      <p className="font-bold text-[var(--text-primary)]">{item.ticker}</p>
      <p className="text-sm text-[var(--text-secondary)]">
        {Math.round(item.value).toLocaleString('zh-TW')} {homeCurrency}
      </p>
      <p className="text-sm text-[var(--accent-peach)]">{item.percentage.toFixed(1)}%</p>
    </div>
  );
};

// Custom legend component
const CustomLegend = ({
  payload,
}: {
  payload?: Array<{ value: string; color: string }>;
}) => {
  if (!payload) return null;

  return (
    <div className="flex flex-wrap justify-center gap-3 mt-4">
      {payload.map((entry, index) => (
        <div key={`legend-${index}`} className="flex items-center gap-1.5">
          <div
            className="w-3 h-3 rounded-full"
            style={{ backgroundColor: entry.color }}
          />
          <span className="text-xs text-[var(--text-secondary)]">{entry.value}</span>
        </div>
      ))}
    </div>
  );
};

export function AssetAllocationPieChart({
  data,
  homeCurrency = 'TWD',
}: AssetAllocationPieChartProps) {
  if (data.length === 0) {
    return (
      <div className="flex items-center justify-center h-64">
        <p className="text-[var(--text-muted)]">獲取報價後顯示資產配置</p>
      </div>
    );
  }

  return (
    <div className="w-full h-64">
      <ResponsiveContainer width="100%" height="100%">
        <PieChart>
          <Pie
            data={data}
            dataKey="value"
            nameKey="ticker"
            cx="50%"
            cy="50%"
            innerRadius={40}
            outerRadius={80}
            paddingAngle={2}
            label={({ name, percent }: { name?: string; percent?: number }) =>
              (percent ?? 0) >= 0.05 ? `${name}` : ''
            }
            labelLine={false}
          >
            {data.map((_, index) => (
              <Cell
                key={`cell-${index}`}
                fill={getChartColor(index)}
                stroke="var(--bg-primary)"
                strokeWidth={2}
              />
            ))}
          </Pie>
          <Tooltip content={<CustomTooltip homeCurrency={homeCurrency} />} />
          <Legend content={<CustomLegend />} />
        </PieChart>
      </ResponsiveContainer>
    </div>
  );
}
