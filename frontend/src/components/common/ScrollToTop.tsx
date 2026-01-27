import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';

/**
 * ScrollToTop
 *
 * 監聽路由變化，當切換頁面時自動將卷軸重置到頂部。
 * 這是 SPA (Single Page Application) 的標準行為模式。
 */
export function ScrollToTop() {
  const { pathname } = useLocation();

  useEffect(() => {
    // 切換頁面時捲動到頂部
    window.scrollTo(0, 0);
  }, [pathname]);

  return null;
}
