import {
  createContext,
  useContext,
  useState,
  useEffect,
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
  const [user, setUser] = useState<User | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  // 掛載時從 localStorage 載入使用者資料
  useEffect(() => {
    const storedUser = localStorage.getItem(USER_KEY);
    const token = localStorage.getItem(TOKEN_KEY);

    if (storedUser && token) {
      try {
        setUser(JSON.parse(storedUser));
      } catch {
        clearAuthData();
      }
    }
    setIsLoading(false);
  }, []);

  const clearAuthData = () => {
    // 清除驗證 token
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
    localStorage.removeItem(USER_KEY);

    // 清除所有使用者相關的快取資料，防止帳號間資料洩漏
    // - quote_cache_: 報價快取
    // - perf_cache_: 績效快取
    // - xirr_cache_: XIRR 快取
    // - rate_cache_: 匯率快取
    removeLocalStorageByPrefixes([
      'quote_cache_',
      'perf_cache_',
      'xirr_cache_',
      'rate_cache_',
    ]);

    // 清除市場資料快取（登入時應重新取得）
    localStorage.removeItem('ytd_data_cache');
    localStorage.removeItem('cape_data_cache');

    // 移除導覽快取（不應跨帳號保留）
    localStorage.removeItem('default_portfolio_id');
    localStorage.removeItem('selected_portfolio_id');

    // 使用者偏好設定（ytd_benchmark_preferences, cape_region_preferences）
    // 現在存在後端 database，但 localStorage 也有快取副本
    // 這裡不清除 localStorage 副本，讓 API 同步來處理

    setUser(null);
  };

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
