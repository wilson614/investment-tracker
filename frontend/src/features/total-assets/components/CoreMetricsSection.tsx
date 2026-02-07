import type { TotalAssetsSummary } from '../types';
import { MetricCard } from './MetricCard';

interface CoreMetricsSectionProps {
  data?: Pick<TotalAssetsSummary, 'investmentRatio' | 'stockRatio'>;
}

function formatRatioAsPercentage(value: number | undefined): string {
  if (typeof value !== 'number' || Number.isNaN(value)) {
    return '0%';
  }

  const percentage = value * 100;
  const roundedValue = Math.round(percentage * 10) / 10;

  if (Number.isInteger(roundedValue)) {
    return `${roundedValue.toFixed(0)}%`;
  }

  return `${roundedValue.toFixed(1)}%`;
}

export function CoreMetricsSection({ data }: CoreMetricsSectionProps) {
  const investmentRatio = data?.investmentRatio ?? 0;
  const stockRatio = data?.stockRatio ?? 0;

  return (
    <section className="grid grid-cols-1 sm:grid-cols-2 gap-4">
      <MetricCard label="投資比例" value={formatRatioAsPercentage(investmentRatio)} />
      <MetricCard label="股票佔比" value={formatRatioAsPercentage(stockRatio)} />
    </section>
  );
}
