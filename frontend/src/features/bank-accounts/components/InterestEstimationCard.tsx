import { TrendingUp } from 'lucide-react';
import { formatCurrency } from '../../../utils/currency';

interface InterestEstimationCardProps {
  yearlyInterest: number;
  monthlyInterest: number;
}

export function InterestEstimationCard({ yearlyInterest, monthlyInterest }: InterestEstimationCardProps) {
  return (
    <div className="metric-card metric-card-cream">
      <div className="flex items-center gap-3 mb-4">
        <div className="p-2 bg-[var(--accent-cream)] rounded-lg text-[var(--bg-primary)]">
          <TrendingUp className="w-5 h-5" />
        </div>
        <span className="text-sm font-medium text-[var(--accent-cream)] uppercase tracking-wider">
          利息預估
        </span>
      </div>

      <div className="grid grid-cols-2 gap-4 border-t border-[rgba(0,0,0,0.1)] dark:border-[rgba(255,255,255,0.1)] pt-4">
        <div>
          <p className="text-xs font-medium text-[var(--text-secondary)] mb-1">預估年利息</p>
          <p className="text-xl font-bold text-[var(--text-primary)]">
            {formatCurrency(yearlyInterest, 'TWD')}
          </p>
        </div>
        <div>
          <p className="text-xs font-medium text-[var(--text-secondary)] mb-1">平均月利息</p>
          <p className="text-xl font-bold text-[var(--text-primary)]">
            {formatCurrency(monthlyInterest, 'TWD')}
          </p>
        </div>
      </div>
    </div>
  );
}
