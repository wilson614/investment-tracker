/**
 * 日期格式化工具
 *
 * 提供台灣時區 (UTC+8) 的日期時間格式化函數。
 */

/**
 * 將 UTC 日期時間轉換為台灣時間字串
 * @param dateStr ISO 8601 格式的日期字串
 * @param options 格式化選項
 * @returns 格式化後的台灣時間字串
 */
export function formatToTaiwanTime(
  dateStr: string | Date,
  options: {
    showTime?: boolean;
    showSeconds?: boolean;
  } = {}
): string {
  const { showTime = false, showSeconds = false } = options;

  const date = typeof dateStr === 'string' ? new Date(dateStr) : dateStr;

  if (isNaN(date.getTime())) {
    return '-';
  }

  // 使用 Intl.DateTimeFormat 以台灣時區格式化
  const dateOptions: Intl.DateTimeFormatOptions = {
    timeZone: 'Asia/Taipei',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  };

  if (showTime) {
    dateOptions.hour = '2-digit';
    dateOptions.minute = '2-digit';
    if (showSeconds) {
      dateOptions.second = '2-digit';
    }
    dateOptions.hour12 = false;
  }

  return new Intl.DateTimeFormat('zh-TW', dateOptions).format(date);
}

/**
 * 格式化為簡短日期 (MM/DD)
 * @param dateStr ISO 8601 格式的日期字串
 * @returns 格式化後的簡短日期字串
 */
export function formatShortDate(dateStr: string | Date): string {
  const date = typeof dateStr === 'string' ? new Date(dateStr) : dateStr;

  if (isNaN(date.getTime())) {
    return '-';
  }

  return new Intl.DateTimeFormat('zh-TW', {
    timeZone: 'Asia/Taipei',
    month: 'numeric',
    day: 'numeric',
  }).format(date);
}

/**
 * 格式化為完整日期 (YYYY/MM/DD)
 * @param dateStr ISO 8601 格式的日期字串
 * @returns 格式化後的完整日期字串
 */
export function formatFullDate(dateStr: string | Date): string {
  return formatToTaiwanTime(dateStr, { showTime: false });
}

/**
 * 格式化為完整日期時間 (YYYY/MM/DD HH:mm)
 * @param dateStr ISO 8601 格式的日期字串
 * @returns 格式化後的完整日期時間字串
 */
export function formatDateTime(dateStr: string | Date): string {
  return formatToTaiwanTime(dateStr, { showTime: true });
}
