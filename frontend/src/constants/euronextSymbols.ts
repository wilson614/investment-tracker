/**
 * Euronext stock symbol to ISIN/MIC mapping.
 * For stocks listed on Euronext exchanges that require ISIN and MIC for quote fetching.
 */
export interface EuronextSymbol {
  isin: string;
  mic: string;
  name?: string;
  currency: string;
}

/**
 * Known Euronext-listed symbols with their ISIN and MIC codes.
 * MIC codes: XAMS (Amsterdam), XPAR (Paris), XBRU (Brussels), XLIS (Lisbon), XAMC (Amsterdam Currency)
 *
 * Note: Some USD-denominated ETFs on Euronext use XAMC (Amsterdam Currency segment) instead of XAMS.
 * Use Euronext search API to verify: https://live.euronext.com/en/instrumentSearch/searchJSON?q={ticker}
 */
export const EURONEXT_SYMBOLS: Record<string, EuronextSymbol> = {
  // iShares Core Global Aggregate Bond UCITS ETF USD (Acc) - USD denominated on Amsterdam
  'AGAC': {
    isin: 'IE000FHBZDZ8',
    mic: 'XAMC',  // Amsterdam Currency segment for USD-denominated ETFs
    name: 'iShares Core Global Aggregate Bond UCITS ETF USD (Acc)',
    currency: 'USD',
  },
  // iShares MSCI ACWI UCITS ETF USD (Acc) - EUR denominated on Amsterdam
  'SSAC': {
    isin: 'IE00B6R52259',
    mic: 'XAMS',
    name: 'iShares MSCI ACWI UCITS ETF USD (Acc)',
    currency: 'EUR',
  },
};

/**
 * Check if a ticker is a known Euronext symbol.
 */
export const isEuronextSymbol = (ticker: string): boolean => {
  return ticker.toUpperCase() in EURONEXT_SYMBOLS;
};

/**
 * Get Euronext symbol details for a ticker.
 */
export const getEuronextSymbol = (ticker: string): EuronextSymbol | undefined => {
  return EURONEXT_SYMBOLS[ticker.toUpperCase()];
};
