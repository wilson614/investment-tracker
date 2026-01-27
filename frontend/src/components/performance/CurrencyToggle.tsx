export type PerformanceCurrencyMode = 'source' | 'home';

const DEFAULT_STORAGE_KEY = 'performance_currency_mode';

interface CurrencyToggleProps {
  value: PerformanceCurrencyMode;
  onChange: (mode: PerformanceCurrencyMode) => void;
  sourceCurrency?: string | null;
  homeCurrency: string;
  storageKey?: string;
  className?: string;
  disabled?: boolean;
}

export function CurrencyToggle({
  value,
  onChange,
  sourceCurrency,
  homeCurrency,
  storageKey = DEFAULT_STORAGE_KEY,
  className = '',
  disabled = false,
}: CurrencyToggleProps) {
  // 注意：父元件應從 localStorage lazy-init 以避免閃爍
  // 此元件不再於掛載時同步以防止重新渲染

  const sourceLabel = sourceCurrency ? `原幣 (${sourceCurrency})` : '原幣';
  const homeLabel = `本位幣 (${homeCurrency})`;

  const setMode = (mode: PerformanceCurrencyMode) => {
    if (disabled) return;

    try {
      localStorage.setItem(storageKey, mode);
    } catch {
      // 忽略
    }

    onChange(mode);
  };

  const baseButton =
    'px-3 py-1 text-sm font-medium rounded-md transition-colors focus:outline-none focus:ring-2 focus:ring-[var(--accent-peach)]';

  const selected = 'bg-[var(--accent-peach)] text-[var(--bg-primary)]';
  const unselected = 'text-[var(--text-muted)] hover:text-[var(--text-primary)] hover:bg-[var(--bg-tertiary)]';

  return (
    <div
      className={`inline-flex items-center gap-1 p-1 bg-[var(--bg-tertiary)] border border-[var(--border-color)] rounded-lg ${
        disabled ? 'opacity-50 pointer-events-none' : ''
      } ${className}`}
    >
      <button
        type="button"
        onClick={() => setMode('source')}
        aria-pressed={value === 'source'}
        className={`${baseButton} ${value === 'source' ? selected : unselected}`}
      >
        {sourceLabel}
      </button>
      <button
        type="button"
        onClick={() => setMode('home')}
        aria-pressed={value === 'home'}
        className={`${baseButton} ${value === 'home' ? selected : unselected}`}
      >
        {homeLabel}
      </button>
    </div>
  );
}
