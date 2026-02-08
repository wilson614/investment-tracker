import type { TotalAssetsSummary } from '../types';
import { CompactMetricRow } from './CompactMetricRow';

interface CoreMetricsSectionProps {
  data?: Pick<TotalAssetsSummary, 'investmentRatio' | 'stockRatio'>;
}

export function CoreMetricsSection({ data }: CoreMetricsSectionProps) {
  const investmentRatio = data?.investmentRatio ?? 0;
  const stockRatio = data?.stockRatio ?? 0;

  return (
    <section className="card-dark p-3 sm:p-4 space-y-2 w-full h-[120px]">
      <h3 className="text-lg font-semibold text-[var(--text-primary)]">資金配置效率</h3>

      <div className="space-y-2.5">
        <CompactMetricRow
          label="流動資產投資率"
          value={investmentRatio}
          description="投資部位 / 可動用資產（可動用資產 = 總資產 - 不可動用資產）"
          color="peach"
        />
        <CompactMetricRow
          label="持倉水位"
          value={stockRatio}
          description="組合市值 / 投資部位"
          color="lavender"
        />
      </div>
    </section>
  );
}
