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
          description="衡量可動用資金中有多少比例配置於投資用途"
          color="peach"
        />
        <CompactMetricRow
          label="持倉水位"
          value={stockRatio}
          description="衡量投資資金的實際曝險程度，數值越高表示越多資金已進場持有資產"
          color="lavender"
        />
      </div>
    </section>
  );
}
