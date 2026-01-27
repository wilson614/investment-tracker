import {
  createContext,
  useContext,
  useState,
  useCallback,
  type ReactNode,
} from 'react';
import type { User, LoginRequest, RegisterRequest } from '../types';
import { authApi } from '../services/api';

interface AuthContextType {
  user: User | null;
  isLoading: boolean;
  isAuthenticated: boolean;
  login: (data: LoginRequest) => Promise<void>;
  register: (data: RegisterRequest) => Promise<void>;
  logout: () => Promise<void>;
  setUser: (user: User) => void;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

const TOKEN_KEY = 'token';
const REFRESH_TOKEN_KEY = 'refreshToken';
const USER_KEY = 'user';

/**
 * 依據前綴移除 localStorage 中的快取項目
 * @param prefixes 要移除的 key 前綴陣列
 */
const removeLocalStorageByPrefixes = (prefixes: string[]) => {
  const keysToRemove: string[] = [];
  for (let i = 0; i < localStorage.length; i++) {
    const key = localStorage.key(i);
    if (key && prefixes.some(prefix => key.startsWith(prefix))) {
      keysToRemove.push(key);
    }
  }
  keysToRemove.forEach(key => localStorage.removeItem(key));
};

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(() => {
    try {
      const storedUser = localStorage.getItem(USER_KEY);
      const token = localStorage.getItem(TOKEN_KEY);
      if (!storedUser || !token) return null;
      return JSON.parse(storedUser) as User;
    } catch {
      return null;
    }
  });
  const [isLoading] = useState(false);

  const clearAuthData = useCallback(() => {
    // 清除驗證 token
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
    localStorage.removeItem(USER_KEY);

    // 清除所有使用者相關的快取資料，防止帳號間資料洩漏
    // - quote_cache_: 報價快取
    // - perf_cache_: 績效快取（舊格式）
    // - perf_years_: 績效年份快取
    // - perf_data_: 績效資料快取
    // - xirr_cache_: XIRR 快取
    // - rate_cache_: 匯率快取
    // - custom_benchmark_returns_: 自訂基準報酬快取
    // - system_benchmark_returns_: 系統基準報酬快取
    removeLocalStorageByPrefixes([
      'quote_cache_',
      'perf_cache_',
      'perf_years_',
      'perf_data_',
      'xirr_cache_',
      'rate_cache_',
      'custom_benchmark_returns_',
      'system_benchmark_returns_',
    ]);

    // 清除使用者專屬市場資料快取
    localStorage.removeItem('custom_benchmark_ytd_cache');
    localStorage.removeItem('custom_benchmarks_list');
    localStorage.removeItem('performance_currency_mode');

    // 注意：ytd_data_cache 和 cape_data_cache 是公開市場資料，不含使用者私人資訊
    // 不需要在登出時清除，可以跨帳號共用以加速載入

    // 移除導覽快取（不應跨帳號保留）
    localStorage.removeItem('default_portfolio_id');
    localStorage.removeItem('selected_portfolio_id');

    // 清除使用者偏好設定快取（防止不同帳號間資料洩漏）
    localStorage.removeItem('ytd_benchmark_preferences');
    localStorage.removeItem('cape_region_preferences');

    // 清除外幣帳本快取（使用者專屬資料）
    localStorage.removeItem('currency_ledgers_cache');

    setUser(null);
  }, []);


  const saveAuthData = (accessToken: string, refreshToken: string, userData: User) => {
    localStorage.setItem(TOKEN_KEY, accessToken);
    localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
    localStorage.setItem(USER_KEY, JSON.stringify(userData));
    setUser(userData);
  };

  const login = useCallback(async (data: LoginRequest) => {
    const response = await authApi.login(data);
    saveAuthData(response.accessToken, response.refreshToken, response.user);
  }, []);

  const register = useCallback(async (data: RegisterRequest) => {
    const response = await authApi.register(data);
    saveAuthData(response.accessToken, response.refreshToken, response.user);
  }, []);

  const logout = useCallback(async () => {
    const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);
    if (refreshToken) {
      try {
        await authApi.logout(refreshToken);
      } catch {
        // 忽略登出錯誤
      }
    }
    clearAuthData();
  }, []);

  const updateUser = useCallback((userData: User) => {
    localStorage.setItem(USER_KEY, JSON.stringify(userData));
    setUser(userData);
  }, []);

  const value: AuthContextType = {
    user,
    isLoading,
    isAuthenticated: !!user,
    login,
    register,
    logout,
    setUser: updateUser,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextType {
  const context = useContext(AuthContext);
  if (context === undefined) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
