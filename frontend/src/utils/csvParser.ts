/**
 * CSV Parser Utility
 * Parses CSV files with automatic column detection and mapping
 */

export interface ParsedCSV {
  headers: string[];
  rows: string[][];
  rowCount: number;
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

/**
 * Parse a CSV string into headers and rows
 */
export function parseCSV(csvContent: string): ParsedCSV {
  const content = csvContent.replace(/^\uFEFF/, '').trim();
  const lines = content.split(/\r?\n/);
  if (lines.length === 0) {
    return { headers: [], rows: [], rowCount: 0 };
  }

  const headers = parseCSVLine(lines[0]);
  const rows: string[][] = [];

  for (let i = 1; i < lines.length; i++) {
    const line = lines[i].trim();
    if (line) {
      rows.push(parseCSVLine(line));
    }
  }

  return {
    headers,
    rows,
    rowCount: rows.length,
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
      normalizeHeader(a)
    );

    for (const header of csvHeaders) {
      const normalizedHeader = normalizeHeader(header);

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

/**
 * Normalize header for comparison (lowercase, remove spaces and special chars)
 */
function normalizeHeader(header: string): string {
  return header
    .replace(/^\uFEFF/, '')
    .toLowerCase()
    // Remove unit/currency suffix like (USD) / （USD） for matching
    .replace(/\([^)]*\)/g, '')
    .replace(/（[^）]*）/g, '')
    .replace(/\[[^\]]*\]/g, '')
    .replace(/\{[^}]*\}/g, '')
    .replace(/[_\s.-]/g, '')
    .trim();
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
 * Parse number string (handle comma separators, currency symbols)
 */
export function parseNumber(numStr: string): number | null {
  if (!numStr || numStr.trim() === '' || numStr.trim() === '-') {
    return null;
  }

  // Remove currency symbols and whitespace
  const cleaned = numStr
    .replace(/[$€£¥₩NT\s]/g, '')
    .replace(/,/g, '')
    .trim();

  const num = parseFloat(cleaned);
  return isNaN(num) ? null : num;
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
