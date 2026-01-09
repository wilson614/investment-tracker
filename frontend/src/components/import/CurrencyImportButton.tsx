import { useState } from 'react';
import { Upload } from 'lucide-react';
import { CSVImportModal, type FieldDefinition } from './CSVImportModal';
import { currencyTransactionApi } from '../../services/api';
import {
  getRowValue,
  parseDate,
  parseNumber,
  formatDateISO,
  type ParsedCSV,
  type ColumnMapping,
  type ParseError,
} from '../../utils/csvParser';
import { CurrencyTransactionType } from '../../types';
import type { CreateCurrencyTransactionRequest } from '../../types';

interface CurrencyImportButtonProps {
  ledgerId: string;
  onImportComplete: () => void;
  /** If provided, renders custom trigger instead of default button */
  renderTrigger?: (onClick: () => void) => React.ReactNode;
}

// Field definitions for currency transaction CSV
const currencyFields: FieldDefinition[] = [
  {
    name: 'date',
    label: '日期',
    aliases: ['transactionDate', 'transaction_date', 'Date', '交易日期', '日期'],
    required: true,
  },
  {
    name: 'type',
    label: '交易類型',
    aliases: ['transactionType', 'transaction_type', 'Type', '類型', '種類'],
    required: true,
  },
  {
    name: 'foreignAmount',
    label: '外幣金額',
    aliases: ['foreign_amount', 'ForeignAmount', 'amount', 'Amount', '外幣', '金額'],
    required: true,
  },
  {
    name: 'homeAmount',
    label: '台幣金額',
    aliases: ['home_amount', 'HomeAmount', 'twdAmount', 'TWDAmount', '台幣', 'TWD'],
    required: false,
  },
  {
    name: 'exchangeRate',
    label: '匯率',
    aliases: ['exchange_rate', 'ExchangeRate', 'rate', 'Rate', '匯率'],
    required: false,
  },
  {
    name: 'notes',
    label: '備註',
    aliases: ['Notes', 'memo', 'Memo', 'description', 'Description', '備註', '說明'],
    required: false,
  },
];

// Map transaction type string to enum
function parseTransactionType(typeStr: string): CurrencyTransactionType | null {
  const normalized = typeStr.toLowerCase().trim();

  // Chinese mappings
  if (normalized.includes('買') || normalized.includes('buy')) {
    return CurrencyTransactionType.ExchangeBuy;
  }
  if (normalized.includes('賣') || normalized.includes('sell')) {
    return CurrencyTransactionType.ExchangeSell;
  }
  if (normalized.includes('利息') || normalized.includes('interest')) {
    return CurrencyTransactionType.Interest;
  }
  if (normalized.includes('消費') || (normalized.includes('支出') && !normalized.includes('其他')) || normalized.includes('spend')) {
    return CurrencyTransactionType.Spend;
  }
  if (normalized.includes('初始') || normalized.includes('期初') || normalized.includes('initial') || normalized.includes('balance')) {
    return CurrencyTransactionType.InitialBalance;
  }
  if (normalized.includes('其他收入') || normalized.includes('其他入') || normalized.includes('獎勵') || normalized.includes('股利') || normalized.includes('bonus') || normalized.includes('dividend') || normalized.includes('other income')) {
    return CurrencyTransactionType.OtherIncome;
  }
  if (normalized.includes('其他支出') || normalized.includes('轉出') || normalized.includes('費用') || normalized.includes('transfer') || normalized.includes('fee') || normalized.includes('other expense')) {
    return CurrencyTransactionType.OtherExpense;
  }

  // Numeric mappings
  const num = parseInt(normalized);
  if (num >= 1 && num <= 7) {
    return num as CurrencyTransactionType;
  }

  return null;
}

export function CurrencyImportButton({
  ledgerId,
  onImportComplete,
  renderTrigger,
}: CurrencyImportButtonProps) {
  const [isModalOpen, setIsModalOpen] = useState(false);

  const handleOpenImport = () => setIsModalOpen(true);

  const handleImport = async (
    csvData: ParsedCSV,
    mapping: ColumnMapping
  ): Promise<{ success: boolean; errors: ParseError[] }> => {
    const errors: ParseError[] = [];
    let successCount = 0;

    for (let i = 0; i < csvData.rows.length; i++) {
      const row = csvData.rows[i];
      const rowNum = i + 2; // 1-based, skip header row

      try {
        // Parse date
        const dateStr = getRowValue(row, csvData.headers, mapping, 'date');
        if (!dateStr) {
          errors.push({ row: rowNum, column: '日期', message: '日期為必填欄位' });
          continue;
        }
        const parsedDate = parseDate(dateStr);
        if (!parsedDate) {
          errors.push({ row: rowNum, column: '日期', message: `無法解析日期: ${dateStr}` });
          continue;
        }

        // Parse type
        const typeStr = getRowValue(row, csvData.headers, mapping, 'type');
        if (!typeStr) {
          errors.push({ row: rowNum, column: '類型', message: '交易類型為必填欄位' });
          continue;
        }
        const transactionType = parseTransactionType(typeStr);
        if (transactionType === null) {
          errors.push({ row: rowNum, column: '類型', message: `無法辨識交易類型: ${typeStr}` });
          continue;
        }

        // Parse foreign amount
        const foreignAmountStr = getRowValue(row, csvData.headers, mapping, 'foreignAmount');
        if (!foreignAmountStr) {
          errors.push({ row: rowNum, column: '外幣金額', message: '外幣金額為必填欄位' });
          continue;
        }
        const foreignAmount = parseNumber(foreignAmountStr);
        if (foreignAmount === null || foreignAmount === 0) {
          errors.push({ row: rowNum, column: '外幣金額', message: `無效的金額: ${foreignAmountStr}` });
          continue;
        }

        // Parse optional fields
        const homeAmountStr = getRowValue(row, csvData.headers, mapping, 'homeAmount');
        const exchangeRateStr = getRowValue(row, csvData.headers, mapping, 'exchangeRate');
        const notes = getRowValue(row, csvData.headers, mapping, 'notes');

        const homeAmount = homeAmountStr ? parseNumber(homeAmountStr) : undefined;
        let exchangeRate = exchangeRateStr ? parseNumber(exchangeRateStr) : undefined;

        // Exchange types need home amount, and we auto-calculate exchange rate
        const isExchangeType =
          transactionType === CurrencyTransactionType.ExchangeBuy ||
          transactionType === CurrencyTransactionType.ExchangeSell;

        // Initial balance needs home amount (cost basis) but not exchange rate
        const needsHomeCost =
          isExchangeType || transactionType === CurrencyTransactionType.InitialBalance;

        if (needsHomeCost && !homeAmount) {
          errors.push({
            row: rowNum,
            message: isExchangeType ? '換匯交易需要提供台幣金額' : '轉入餘額需要提供台幣成本',
          });
          continue;
        }

        // Validate TWD amount is an integer
        if (homeAmount !== undefined && !Number.isInteger(homeAmount)) {
          errors.push({
            row: rowNum,
            column: '台幣金額',
            message: '台幣金額必須為整數',
          });
          continue;
        }

        // Auto-calculate exchange rate for exchange types if not provided
        if (isExchangeType && homeAmount && foreignAmount && !exchangeRate) {
          exchangeRate = homeAmount / Math.abs(foreignAmount);
        }

        // Build request
        const request: CreateCurrencyTransactionRequest = {
          currencyLedgerId: ledgerId,
          transactionDate: formatDateISO(parsedDate),
          transactionType,
          foreignAmount: Math.abs(foreignAmount),
          homeAmount: homeAmount ? Math.abs(homeAmount) : undefined,
          exchangeRate: isExchangeType && exchangeRate != null ? exchangeRate : undefined,
          notes: notes || undefined,
        };

        // Create transaction
        await currencyTransactionApi.create(request);
        successCount++;
      } catch (err) {
        errors.push({
          row: rowNum,
          message: err instanceof Error ? err.message : '建立交易失敗',
        });
      }
    }

    if (successCount > 0) {
      onImportComplete();
    }

    return {
      success: errors.length === 0,
      errors,
    };
  };

  return (
    <>
      {renderTrigger ? (
        renderTrigger(handleOpenImport)
      ) : (
        <button
          onClick={handleOpenImport}
          className="btn-dark flex items-center gap-2 px-3 py-1.5 text-sm"
        >
          <Upload className="w-3.5 h-3.5" />
          匯入
        </button>
      )}

      <CSVImportModal
        isOpen={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        title="匯入外幣交易"
        fields={currencyFields}
        onImport={handleImport}
      />
    </>
  );
}
