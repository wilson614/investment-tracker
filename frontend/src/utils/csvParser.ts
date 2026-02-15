/**
 * CSV Parser Utility
 * Parses CSV files with automatic column detection and mapping
 */

export type StockImportFormat = 'legacy_csv' | 'broker_statement' | 'unknown';

export interface ParsedCSVRowMetadata {
  /**
   * Original CSV row number (1-based, including header row).
   * First data row is 2.
   */
  originalRowNumber: number;
  /** 0-based index in parsed data rows array */
  originalIndex: number;
  /** Raw source line text before parsing */
  sourceLine: string;
}

export interface ParsedCSV {
  headers: string[];
  rows: string[][];
  rowCount: number;
  /** Row-order metadata for preserving original file sequence */
  rowMetadata: ParsedCSVRowMetadata[];
}

export interface ColumnMapping {
  [targetField: string]: string | null; // CSV header -> target field
}

export interface ParseError {
  row: number;
  column?: string;
  message: string;
}

export interface ParseResult<T> {
  success: boolean;
  data: T[];
  errors: ParseError[];
  totalRows: number;
  successCount: number;
  errorCount: number;
}

export const LEGACY_STOCK_FIELD_ALIASES = {
  date: ['transactionDate', 'transaction_date', 'Date', '交易日期', '日期', '買進日期'],
  ticker: ['Ticker', 'symbol', 'Symbol', 'stock', 'Stock', '代碼', '股票', '股票代號'],
  type: ['transactionType', 'transaction_type', 'Type', '類型', '買賣', '交易別'],
  market: ['Market', 'exchange', 'Exchange', '市場', '交易所'],
  currency: ['Currency', 'currencyCode', 'currency_code', '幣別', '貨幣'],
  shares: ['Shares', 'quantity', 'Quantity', 'qty', 'Qty', '數量', '股', '買進股數', '股數'],
  price: ['pricePerShare', 'price_per_share', 'Price', '價格', '單價', '買進價格'],
  fees: ['Fees', 'commission', 'Commission', 'fee', 'Fee', '手續費', '費用'],
  notes: ['Notes', 'memo', 'Memo', 'description', 'Description', '備註', '說明'],
} as const;

export const BROKER_STATEMENT_FIELD_ALIASES = {
  securityName: ['股名', '股票名稱', '證券名稱', '標的名稱', '商品名稱'],
  tradeDate: ['日期', '成交日期', '交易日期'],
  quantity: ['成交股數', '成交數量', '股數', '數量'],
  netSettlement: ['淨收付', '淨交割金額', '淨收付金額', '淨額'],
  unitPrice: ['成交單價', '成交價格', '單價'],
  grossAmount: ['成交價金', '成交金額'],
  fees: ['手續費', '費用', '佣金'],
  taxes: ['交易稅', '稅款', '證交稅'],
  currency: ['幣別', '交易幣別', '貨幣'],
  ticker: ['代碼', '股票代號', '證券代號'],
  notes: ['備註', '說明'],
} as const;

/**
 * Parse a CSV string into headers and rows
 */
export function parseCSV(csvContent: string): ParsedCSV {
  const content = csvContent.replace(/^\uFEFF/, '').trim();
  const lines = content.split(/\r?\n/);
  if (lines.length === 0) {
    return { headers: [], rows: [], rowCount: 0, rowMetadata: [] };
  }

  const headers = parseCSVLine(lines[0]);
  const rows: string[][] = [];
  const rowMetadata: ParsedCSVRowMetadata[] = [];

  for (let i = 1; i < lines.length; i++) {
    const sourceLine = lines[i];
    const line = sourceLine.trim();
    if (line) {
      rows.push(parseCSVLine(line));
      rowMetadata.push({
        originalRowNumber: i + 1,
        originalIndex: rowMetadata.length,
        sourceLine,
      });
    }
  }

  return {
    headers,
    rows,
    rowCount: rows.length,
    rowMetadata,
  };
}

/**
 * Parse a single CSV line, handling quoted values
 */
function parseCSVLine(line: string): string[] {
  const result: string[] = [];
  let current = '';
  let inQuotes = false;

  for (let i = 0; i < line.length; i++) {
    const char = line[i];
    const nextChar = line[i + 1];

    if (inQuotes) {
      if (char === '"' && nextChar === '"') {
        // Escaped quote
        current += '"';
        i++; // Skip next quote
      } else if (char === '"') {
        // End of quoted section
        inQuotes = false;
      } else {
        current += char;
      }
    } else {
      if (char === '"') {
        // Start of quoted section
        inQuotes = true;
      } else if (char === ',') {
        // Field separator
        result.push(current.trim());
        current = '';
      } else {
        current += char;
      }
    }
  }

  // Don't forget the last field
  result.push(current.trim());

  return result;
}

/**
 * Normalize header for comparison (lowercase, remove spaces and special chars)
 */
export function normalizeHeaderForMatching(header: string): string {
  return header
    .replace(/^\uFEFF/, '')
    .toLowerCase()
    // Remove unit/currency suffix like (USD) / (USD) for matching
    .replace(/\([^)]*\)/g, '')
    .replace(/（[^）]*）/g, '')
    .replace(/\[[^\]]*\]/g, '')
    .replace(/\{[^}]*\}/g, '')
    .replace(/[＿_\s.-]/g, '')
    .trim();
}

/**
 * Auto-detect column mapping based on header names
 */
export function autoMapColumns(
  csvHeaders: string[],
  targetFields: { name: string; aliases: string[] }[]
): ColumnMapping {
  const mapping: ColumnMapping = {};

  for (const target of targetFields) {
    let matched = false;

    // Normalize target name and aliases for comparison
    const normalizedAliases = [target.name, ...target.aliases].map((a) =>
      normalizeHeaderForMatching(a)
    );

    for (const header of csvHeaders) {
      const normalizedHeader = normalizeHeaderForMatching(header);

      if (normalizedAliases.includes(normalizedHeader)) {
        mapping[target.name] = header;
        matched = true;
        break;
      }
    }

    if (!matched) {
      mapping[target.name] = null;
    }
  }

  return mapping;
}

function countAliasMatches(headers: string[], aliases: readonly string[]): number {
  const normalizedHeaders = headers.map((header) => normalizeHeaderForMatching(header));
  const normalizedAliasSet = new Set(aliases.map((alias) => normalizeHeaderForMatching(alias)));

  return normalizedHeaders.reduce((count, header) => {
    if (normalizedAliasSet.has(header)) {
      return count + 1;
    }
    return count;
  }, 0);
}

/**
 * Detect stock import format from CSV headers.
 */
export function detectStockImportFormat(headers: string[]): StockImportFormat {
  if (headers.length === 0) {
    return 'unknown';
  }

  const legacyAliasCandidates = [
    ...LEGACY_STOCK_FIELD_ALIASES.date,
    ...LEGACY_STOCK_FIELD_ALIASES.ticker,
    ...LEGACY_STOCK_FIELD_ALIASES.type,
    ...LEGACY_STOCK_FIELD_ALIASES.shares,
    ...LEGACY_STOCK_FIELD_ALIASES.price,
    ...LEGACY_STOCK_FIELD_ALIASES.market,
    ...LEGACY_STOCK_FIELD_ALIASES.currency,
  ];

  const brokerAliasCandidates = [
    ...BROKER_STATEMENT_FIELD_ALIASES.securityName,
    ...BROKER_STATEMENT_FIELD_ALIASES.tradeDate,
    ...BROKER_STATEMENT_FIELD_ALIASES.quantity,
    ...BROKER_STATEMENT_FIELD_ALIASES.netSettlement,
    ...BROKER_STATEMENT_FIELD_ALIASES.unitPrice,
    ...BROKER_STATEMENT_FIELD_ALIASES.fees,
    ...BROKER_STATEMENT_FIELD_ALIASES.taxes,
    ...BROKER_STATEMENT_FIELD_ALIASES.currency,
  ];

  const legacyScore = countAliasMatches(headers, legacyAliasCandidates);
  const brokerScore = countAliasMatches(headers, brokerAliasCandidates);

  if (brokerScore >= 3 && brokerScore >= legacyScore) {
    return 'broker_statement';
  }

  if (legacyScore >= 3) {
    return 'legacy_csv';
  }

  return 'unknown';
}

/**
 * Normalize text cell value by trimming and stripping surrounding quotes.
 */
export function normalizeCsvCellValue(value: string | null | undefined): string {
  if (!value) {
    return '';
  }

  return value.trim().replace(/^"(.*)"$/, '$1').trim();
}

/**
 * Normalize user/manual ticker input.
 */
export function normalizeTickerValue(value: string | null | undefined): string | null {
  const normalized = normalizeCsvCellValue(value)
    .replace(/\s+/g, '')
    .toUpperCase();

  return normalized.length > 0 ? normalized : null;
}

/**
 * Normalize broker/localized currency label to canonical currency code.
 */
export function normalizeCurrencyCode(value: string | null | undefined): string | null {
  const normalized = normalizeCsvCellValue(value).toUpperCase();
  if (!normalized) {
    return null;
  }

  if (
    normalized === '台幣'.toUpperCase() ||
    normalized === '臺幣'.toUpperCase() ||
    normalized === 'TWD' ||
    normalized === 'NTD' ||
    normalized === 'NT$'
  ) {
    return 'TWD';
  }

  if (normalized === '美元'.toUpperCase() || normalized === 'USD' || normalized === 'US$' || normalized === '$') {
    return 'USD';
  }

  if (normalized === '歐元'.toUpperCase() || normalized === 'EUR' || normalized === '€') {
    return 'EUR';
  }

  if (normalized === '英鎊'.toUpperCase() || normalized === 'GBP' || normalized === '£') {
    return 'GBP';
  }

  return normalized;
}

/**
 * Get value from a row using column mapping
 */
export function getRowValue(
  row: string[],
  headers: string[],
  mapping: ColumnMapping,
  fieldName: string
): string | undefined {
  const csvHeader = mapping[fieldName];
  if (!csvHeader) return undefined;

  const index = headers.indexOf(csvHeader);
  if (index === -1) return undefined;

  return row[index];
}

/**
 * Parse date string in various formats
 */
export function parseDate(dateStr: string): Date | null {
  if (!dateStr) return null;

  // Try ISO format first (YYYY-MM-DD)
  const isoMatch = dateStr.match(/^(\d{4})-(\d{1,2})-(\d{1,2})/);
  if (isoMatch) {
    return new Date(
      parseInt(isoMatch[1]),
      parseInt(isoMatch[2]) - 1,
      parseInt(isoMatch[3])
    );
  }

  // Try slash format (YYYY/MM/DD or MM/DD/YYYY)
  const slashMatch = dateStr.match(/^(\d{1,4})\/(\d{1,2})\/(\d{1,4})/);
  if (slashMatch) {
    if (slashMatch[1].length === 4) {
      // YYYY/MM/DD
      return new Date(
        parseInt(slashMatch[1]),
        parseInt(slashMatch[2]) - 1,
        parseInt(slashMatch[3])
      );
    } else if (slashMatch[3].length === 4) {
      // MM/DD/YYYY
      return new Date(
        parseInt(slashMatch[3]),
        parseInt(slashMatch[1]) - 1,
        parseInt(slashMatch[2])
      );
    }
  }

  // Try native Date parsing as fallback
  const parsed = new Date(dateStr);
  if (!isNaN(parsed.getTime())) {
    return parsed;
  }

  return null;
}

/**
 * Parse number string (handle comma separators, currency symbols and localized negatives)
 */
export function parseNumber(numStr: string): number | null {
  if (!numStr || numStr.trim() === '' || numStr.trim() === '-') {
    return null;
  }

  const raw = numStr.trim();
  const isParenthesizedNegative = /^\(.*\)$/.test(raw) || /^（.*）$/.test(raw);

  // Remove wrappers and locale/currency artifacts
  const cleaned = raw
    .replace(/^[（(]\s*/, '')
    .replace(/\s*[）)]$/, '')
    .replace(/[＋+]/g, '')
    .replace(/[−﹣－]/g, '-')
    .replace(/[，,]/g, '')
    .replace(/NT\$/gi, '')
    .replace(/台幣|臺幣|美元|歐元|英鎊|元/gi, '')
    .replace(/[$€£¥₩\s]/g, '')
    .trim();

  const num = parseFloat(cleaned);
  if (isNaN(num)) {
    return null;
  }

  if (isParenthesizedNegative) {
    return -Math.abs(num);
  }

  return num;
}

/**
 * Format date to ISO string (YYYY-MM-DD)
 */
export function formatDateISO(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}
