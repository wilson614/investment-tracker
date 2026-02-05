export * from './chartColors';
export * from './currencies';

/**
 * Default selected benchmarks shared across pages (English keys to match backend).
 * Mirrors Dashboard MarketYtdSection's default selection (max 10).
 */
export const DEFAULT_BENCHMARKS = [
  'All Country', 'US Large', 'Developed Markets Large', 'Developed Markets Small',
  'Dev ex US Large', 'Emerging Markets', 'Europe', 'Japan', 'China', 'Taiwan 0050',
] as const;
