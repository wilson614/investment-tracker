type CurrencyFormatConfig = {
  symbol: string;
  fractionDigits: number;
  spaceAfterSymbol?: boolean;
};

const CURRENCY_FORMATS: Record<string, CurrencyFormatConfig> = {
  TWD: { symbol: 'NT$', fractionDigits: 0, spaceAfterSymbol: true },
  USD: { symbol: '$', fractionDigits: 2 },
  EUR: { symbol: '€', fractionDigits: 2 },
  JPY: { symbol: '¥', fractionDigits: 0 },
  CNY: { symbol: '¥', fractionDigits: 2 },
  GBP: { symbol: '£', fractionDigits: 2 },
  AUD: { symbol: 'A$', fractionDigits: 2 },
};

export function formatCurrency(amount: number, currency: string): string {
  const normalizedCurrency = currency.toUpperCase();
  const config = CURRENCY_FORMATS[normalizedCurrency];
  const fractionDigits = config?.fractionDigits ?? 2;

  const formattedAmount = new Intl.NumberFormat('en-US', {
    minimumFractionDigits: fractionDigits,
    maximumFractionDigits: fractionDigits,
  }).format(amount);

  if (!config) {
    return `${normalizedCurrency} ${formattedAmount}`;
  }

  return config.spaceAfterSymbol
    ? `${config.symbol} ${formattedAmount}`
    : `${config.symbol}${formattedAmount}`;
}
