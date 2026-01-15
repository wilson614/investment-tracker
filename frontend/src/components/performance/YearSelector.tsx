import { ChevronDown } from 'lucide-react';

interface YearSelectorProps {
  years: number[];
  selectedYear: number | null;
  currentYear: number;
  onChange: (year: number) => void;
  isLoading?: boolean;
  disabled?: boolean;
  className?: string;
}

export function YearSelector({
  years,
  selectedYear,
  currentYear,
  onChange,
  isLoading = false,
  disabled = false,
  className = '',
}: YearSelectorProps) {
  if (years.length === 0) {
    return (
      <div className={`text-[var(--text-muted)] text-sm ${className}`}>
        無交易資料
      </div>
    );
  }

  const getYearLabel = (year: number) => {
    if (year === currentYear) {
      return `${year} (YTD)`;
    }
    return year.toString();
  };

  return (
    <div className={`relative inline-block ${className}`}>
      <select
        value={selectedYear ?? ''}
        onChange={(e) => onChange(Number(e.target.value))}
        disabled={disabled || isLoading}
        className="appearance-none bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg px-4 py-2 pr-10 text-[var(--text-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-peach)] disabled:opacity-50 cursor-pointer hover:border-[var(--border-hover)] transition-colors"
      >
        <option value="" disabled>選擇年份</option>
        {years.map((year) => (
          <option key={year} value={year}>
            {getYearLabel(year)}
          </option>
        ))}
      </select>
      <ChevronDown className="absolute right-3 top-1/2 -translate-y-1/2 w-4 h-4 text-[var(--text-muted)] pointer-events-none" />
    </div>
  );
}
