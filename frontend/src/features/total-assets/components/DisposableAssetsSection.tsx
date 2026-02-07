import { formatCurrency } from '../../../utils/currency';
import type { TotalAssetsSummary } from '../types';

export interface DisposableAssetsSectionProps
  extends Pick<TotalAssetsSummary, 'portfolioValue' | 'cashBalance' | 'disposableDeposit' | 'investmentRatio' | 'investmentTotal'> {}

function formatRatioAsPercentage(value: number): string {
  if (!Number.isFinite(value)) {
    return '0%';
  }

  const percentage = value * 100;
  const roundedValue = Math.round(percentage * 10) / 10;

  if (Number.isInteger(roundedValue)) {
    return `${roundedValue.toFixed(0)}%`;
  }

  return `${roundedValue.toFixed(1)}%`;
}

export function DisposableAssetsSection({
  portfolioValue,
  cashBalance,
  disposableDeposit,
  investmentRatio,
  investmentTotal,
}: DisposableAssetsSectionProps) {
  const denominator = investmentTotal + disposableDeposit;

  return (
    <section className="card-dark p-6 space-y-5 lg:col-span-2">
      <header className="space-y-1">
        <h3 className="text-lg font-semibold text-[var(--text-primary)]">可動用資產</h3>
        <p className="text-sm text-[var(--text-muted)]">聚焦可動用存款與投資部位，追蹤資金配置效率</p>
      </header>

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
        <article className="rounded-lg border border-[var(--border-color)] p-4 bg-[var(--bg-tertiary)]/50">
          <p className="text-xs uppercase tracking-wider text-[var(--text-muted)]">投資部位總額</p>
          <p className="mt-2 text-xl font-semibold font-mono text-[var(--text-primary)]">
            {formatCurrency(investmentTotal, 'TWD')}
          </p>
          <p className="mt-1 text-xs text-[var(--text-muted)]">
            股票市值 {formatCurrency(portfolioValue, 'TWD')} + 帳本現金 {formatCurrency(cashBalance, 'TWD')}
          </p>
        </article>

        <article className="rounded-lg border border-[var(--border-color)] p-4 bg-[var(--bg-tertiary)]/50">
          <p className="text-xs uppercase tracking-wider text-[var(--text-muted)]">帳本現金</p>
          <p className="mt-2 text-xl font-semibold font-mono text-[var(--text-primary)]">
            {formatCurrency(cashBalance, 'TWD')}
          </p>
        </article>

        <article className="rounded-lg border border-[var(--border-color)] p-4 bg-[var(--bg-tertiary)]/50">
          <p className="text-xs uppercase tracking-wider text-[var(--text-muted)]">可動用存款</p>
          <p className="mt-2 text-xl font-semibold font-mono text-[var(--text-primary)]">
            {formatCurrency(disposableDeposit, 'TWD')}
          </p>
        </article>
      </div>

      <div className="rounded-lg border border-[var(--accent-peach)]/30 bg-[var(--accent-peach-soft)] p-4 space-y-1">
        <p className="text-xs uppercase tracking-wider text-[var(--text-muted)]">Investment Ratio</p>
        <p className="text-2xl font-bold font-mono text-[var(--text-primary)]">{formatRatioAsPercentage(investmentRatio)}</p>
        <p className="text-xs text-[var(--text-muted)]">
          {denominator > 0
            ? `${formatCurrency(investmentTotal, 'TWD')} ÷ (${formatCurrency(investmentTotal, 'TWD')} + ${formatCurrency(disposableDeposit, 'TWD')})`
            : '目前無可計算分母（投資部位與可動用存款皆為 0）'}
        </p>
      </div>
    </section>
  );
}
