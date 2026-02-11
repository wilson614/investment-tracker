/**
 * Error Mapping Utility
 *
 * 用於將後端或套件的英文錯誤訊息映射為使用者友善的繁體中文訊息。
 * 包含常見的 API 錯誤、網路問題、與業務邏輯錯誤。
 */

export const ERROR_MESSAGES: Record<string, string> = {
  // Auth related
  'Session expired. Please login again.': '連線已過期，請重新登入',
  'Access denied': '存取被拒',
  'Unauthorized': '未授權的操作',
  'Invalid credentials': '帳號或密碼錯誤',

  // API & Network
  'Failed to fetch': '網路連線失敗，請檢查您的網路狀態',
  'Network Error': '網路連線錯誤',
  'An unexpected error occurred': '發生未預期的錯誤',
  'Request failed with status code': '請求失敗',
  'Internal Server Error': '伺服器內部錯誤',

  // Business Logic - Portfolio
  'Portfolio not found': '找不到投資組合',
  'Portfolio already exists': '投資組合已存在',

  // Business Logic - Transactions
  'Transaction not found': '找不到交易紀錄',
  'Insufficient balance': '餘額不足',
  'Stock currency does not match bound ledger currency': '股票幣別與帳本綁定幣別不符',

  // Business Logic - Market Data
  'Symbol not found': '找不到此代碼',
  'Market closed': '市場已關閉',

  // Common Validations
  'Field is required': '此欄位為必填',
  'Invalid format': '格式不正確',
  'One or more validation errors occurred.': '輸入資料驗證失敗',
  'Total allocations cannot exceed total bank assets.': '資金配置總額不能超過銀行總資產',
  'Amount cannot be negative': '金額不能為負數',
  'Invalid allocation purpose': '資金配置用途不正確',
  'Note cannot exceed 500 characters': '備註不能超過 500 個字元',
  'A non-empty request body is required.': '請提供請求資料',
  'The JSON value could not be converted to': '輸入格式不正確，請檢查欄位內容',
  'Fund allocation purpose already exists.': '此用途的資金配置已存在',
};

/**
 * 嘗試將英文錯誤訊息轉換為中文。
 * 若無對應映射，則回傳原始訊息。
 *
 * 支援部分比對 (partial match) 以處理包含動態內容的錯誤訊息。
 */
export function getErrorMessage(originalMessage: string): string {
  if (!originalMessage) return '發生未知錯誤';

  // 1. 先嘗試完全比對
  if (ERROR_MESSAGES[originalMessage]) {
    return ERROR_MESSAGES[originalMessage];
  }

  // 2. 嘗試部分比對 (Case insensitive)
  const lowerMsg = originalMessage.toLowerCase();

  if (lowerMsg.includes('session expired') || lowerMsg.includes('token expired')) {
    return ERROR_MESSAGES['Session expired. Please login again.'];
  }

  if (lowerMsg.includes('network error') || lowerMsg.includes('failed to fetch')) {
    return ERROR_MESSAGES['Failed to fetch'];
  }

  if (lowerMsg.includes('validation errors occurred')) {
    return ERROR_MESSAGES['One or more validation errors occurred.'];
  }

  if (lowerMsg.includes('json value could not be converted')) {
    return ERROR_MESSAGES['The JSON value could not be converted to'];
  }

  if (lowerMsg.includes('total allocations cannot exceed total bank assets')) {
    return ERROR_MESSAGES['Total allocations cannot exceed total bank assets.'];
  }

  if (lowerMsg.includes('currency ledger') && lowerMsg.includes('already exists')) {
    return '此幣別的帳本已存在';
  }

  if (lowerMsg.includes('stock currency') && lowerMsg.includes('bound ledger currency')) {
    return ERROR_MESSAGES['Stock currency does not match bound ledger currency'];
  }

  // 特定字詞替換
  if (lowerMsg.includes('not found')) {
    if (lowerMsg.includes('portfolio')) return '找不到投資組合';
    if (lowerMsg.includes('transaction')) return '找不到交易紀錄';
    if (lowerMsg.includes('user')) return '找不到使用者';
  }

  return originalMessage;
}
