import type { TotalAssetsSummary } from '../types';
import { CompactMetricRow } from './CompactMetricRow';

interface CoreMetricsSectionProps {
  data?: Pick<TotalAssetsSummary, 'investmentRatio' | 'stockRatio'>;
}

export function CoreMetricsSection({ data }: CoreMetricsSectionProps) {
  const investmentRatio = data?.investmentRatio ?? 0;
  const stockRatio = data?.stockRatio ?? 0;

  return (
    <section className="card-dark p-4 sm:p-5 space-y-4 w-full h-full">
      <h3 className="text-sm font-semibold text-[var(--text-primary)]">資金配置效率</h3>

      <div className="space-y-4">
        <CompactMetricRow
          label="投資部位對比"
          value={investmentRatio}
          description="投資部位 / (可動用資產 + 投資部位)"
          color="peach"
        />
        <CompactMetricRow
          label="持倉比例"
          value={stockRatio}
          description="組合市值 / 投資部位（越高代表閒置現金越少）"
          color="lavender"
        />
      </div>
    </section>
  );
}
