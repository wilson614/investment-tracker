import { useState } from 'react';
import { ChevronDown, ChevronUp, Landmark, WalletCards } from 'lucide-react';
import { Skeleton } from '../../../components/common/SkeletonLoader';
import { formatCurrency } from '../../../utils/currency';
import { useAvailableFunds } from '../hooks/useAvailableFunds';

export function AvailableFundsSummary() {
  const { summary, isLoading, error } = useAvailableFunds();
  const [isFixedDepositsExpanded, setIsFixedDepositsExpanded] = useState(false);
  const [isInstallmentsExpanded, setIsInstallmentsExpanded] = useState(false);

  if (isLoading) {
    return (
      <section className="card-dark p-6 space-y-5" aria-label="可動用資金摘要載入中">
        <div className="space-y-1">
          <Skeleton width="w-32" height="h-7" />
          <Skeleton width="w-56" height="h-4" />
        </div>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
          {[1, 2, 3].map((item) => (
            <div key={item} className="rounded-lg border border-[var(--border-color)] p-4 bg-[var(--bg-tertiary)]/50">
              <Skeleton width="w-24" height="h-4" />
              <Skeleton width="w-36" height="h-8" className="mt-2" />
            </div>
          ))}
        </div>

        <div className="space-y-3">
          {[1, 2].map((item) => (
            <div key={item} className="rounded-lg border border-[var(--border-color)] p-4 bg-[var(--bg-tertiary)]/30">
              <div className="flex items-center justify-between">
                <Skeleton width="w-32" height="h-5" />
                <Skeleton width="w-24" height="h-5" />
              </div>
            </div>
          ))}
        </div>
      </section>
    );
  }

  if (error) {
    return (
      <section className="card-dark p-6 space-y-3" role="alert">
        <h3 className="text-lg font-semibold text-[var(--text-primary)]">可動用資金</h3>
        <div className="p-3 rounded border border-red-500/40 bg-red-500/10 text-red-200 text-sm">
          可動用資金資料載入失敗，請稍後再試。
        </div>
      </section>
    );
  }

  if (!summary) {
    return (
      <section className="card-dark p-6 space-y-3">
        <h3 className="text-lg font-semibold text-[var(--text-primary)]">可動用資金</h3>
        <p className="text-sm text-[var(--text-muted)]">尚無可動用資金資料。</p>
      </section>
    );
  }

  const {
    totalBankAssets,
    availableFunds,
    committedFunds,
    breakdown,
    currency,
  } = summary;

  const fixedDeposits = breakdown.fixedDeposits;
  const installments = breakdown.installments;

  return (
    <section className="card-dark p-6 space-y-5" aria-label="可動用資金摘要">
      <header className="space-y-1">
        <h3 className="text-lg font-semibold text-[var(--text-primary)]">可動用資金摘要</h3>
        <p className="text-sm text-[var(--text-muted)]">顯示銀行資產、已承諾資金與可動用餘額</p>
      </header>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
        <article className="rounded-lg border border-[var(--border-color)] p-4 bg-[var(--bg-tertiary)]/50">
          <div className="flex items-center gap-2 text-[var(--text-muted)]">
            <Landmark className="w-4 h-4" />
            <p className="text-sm uppercase tracking-wide">銀行總資產</p>
          </div>
          <p className="mt-2 text-2xl font-semibold font-mono text-[var(--text-primary)]">
            {formatCurrency(totalBankAssets, currency)}
          </p>
        </article>

        <article className="rounded-lg border border-[var(--accent-peach)]/50 p-4 bg-[var(--accent-peach)]/10">
          <div className="flex items-center gap-2 text-[var(--accent-peach)]">
            <WalletCards className="w-4 h-4" />
            <p className="text-sm uppercase tracking-wide font-medium">可動用資金</p>
          </div>
          <p className="mt-2 text-3xl font-bold font-mono text-[var(--text-primary)]">
            {formatCurrency(availableFunds, currency)}
          </p>
        </article>

        <article className="rounded-lg border border-[var(--border-color)] p-4 bg-[var(--bg-tertiary)]/50">
          <p className="text-sm uppercase tracking-wide text-[var(--text-muted)]">已承諾資金</p>
          <p className="mt-2 text-2xl font-semibold font-mono text-[var(--text-primary)]">
            {formatCurrency(committedFunds, currency)}
          </p>
          <p className="mt-1 text-xs text-[var(--text-muted)]">定存本金 + 分期未繳餘額</p>
        </article>
      </div>

      <div className="space-y-3">
        <article className="rounded-lg border border-[var(--border-color)] bg-[var(--bg-tertiary)]/30">
          <button
            type="button"
            onClick={() => setIsFixedDepositsExpanded((prev) => !prev)}
            className="w-full p-4 flex items-center justify-between gap-4 text-left hover:bg-[var(--bg-tertiary)]/40 transition-colors"
          >
            <div>
              <p className="text-sm font-medium text-[var(--text-primary)]">定存承諾資金</p>
              <p className="text-sm text-[var(--text-muted)]">
                {formatCurrency(breakdown.fixedDepositsPrincipal, currency)} ・ {fixedDeposits.length} 筆
              </p>
            </div>
            {isFixedDepositsExpanded ? (
              <ChevronUp className="w-4 h-4 text-[var(--text-muted)]" />
            ) : (
              <ChevronDown className="w-4 h-4 text-[var(--text-muted)]" />
            )}
          </button>

          {isFixedDepositsExpanded && (
            <div className="px-4 pb-4 space-y-2">
              {fixedDeposits.length === 0 ? (
                <p className="text-sm text-[var(--text-muted)]">目前沒有定存資料。</p>
              ) : (
                fixedDeposits.map((deposit) => (
                  <div
                    key={deposit.id}
                    className="rounded-lg border border-[var(--border-color)] p-3 bg-[var(--bg-secondary)]"
                  >
                    <div className="flex items-center justify-between gap-3">
                      <p className="text-sm font-medium text-[var(--text-primary)]">{deposit.bankName}</p>
                      <p className="text-sm font-mono text-[var(--text-primary)]">
                        {formatCurrency(deposit.principal, deposit.currency)}
                      </p>
                    </div>
                    <p className="mt-1 text-xs text-[var(--text-muted)]">
                      基準幣別金額：{formatCurrency(deposit.principalInBaseCurrency, currency)}
                    </p>
                  </div>
                ))
              )}
            </div>
          )}
        </article>

        <article className="rounded-lg border border-[var(--border-color)] bg-[var(--bg-tertiary)]/30">
          <button
            type="button"
            onClick={() => setIsInstallmentsExpanded((prev) => !prev)}
            className="w-full p-4 flex items-center justify-between gap-4 text-left hover:bg-[var(--bg-tertiary)]/40 transition-colors"
          >
            <div>
              <p className="text-sm font-medium text-[var(--text-primary)]">分期承諾資金</p>
              <p className="text-sm text-[var(--text-muted)]">
                {formatCurrency(breakdown.unpaidInstallmentBalance, currency)} ・ {installments.length} 筆
              </p>
            </div>
            {isInstallmentsExpanded ? (
              <ChevronUp className="w-4 h-4 text-[var(--text-muted)]" />
            ) : (
              <ChevronDown className="w-4 h-4 text-[var(--text-muted)]" />
            )}
          </button>

          {isInstallmentsExpanded && (
            <div className="px-4 pb-4 space-y-2">
              {installments.length === 0 ? (
                <p className="text-sm text-[var(--text-muted)]">目前沒有分期資料。</p>
              ) : (
                installments.map((installment) => (
                  <div
                    key={installment.id}
                    className="rounded-lg border border-[var(--border-color)] p-3 bg-[var(--bg-secondary)]"
                  >
                    <div className="flex items-center justify-between gap-3">
                      <p className="text-sm font-medium text-[var(--text-primary)]">{installment.description}</p>
                      <p className="text-sm font-mono text-[var(--text-primary)]">
                        {formatCurrency(installment.unpaidBalance, currency)}
                      </p>
                    </div>
                    <p className="mt-1 text-xs text-[var(--text-muted)]">信用卡：{installment.creditCardName}</p>
                  </div>
                ))
              )}
            </div>
          )}
        </article>
      </div>
    </section>
  );
}
