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

  // Business Logic - Market Data
  'Symbol not found': '找不到此代碼',
  'Market closed': '市場已關閉',

  // Common Validations
  'Field is required': '此欄位為必填',
  'Invalid format': '格式不正確',
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

  // 特定字詞替換
  if (lowerMsg.includes('not found')) {
      if (lowerMsg.includes('portfolio')) return '找不到投資組合';
      if (lowerMsg.includes('transaction')) return '找不到交易紀錄';
      if (lowerMsg.includes('user')) return '找不到使用者';
  }

  return originalMessage;
}
