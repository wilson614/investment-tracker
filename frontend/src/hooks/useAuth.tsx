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

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  // Load user from localStorage on mount
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
    // Clear auth tokens
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
    localStorage.removeItem(USER_KEY);

    // Clear all user-specific cached data
    // This prevents data leakage between accounts
    const keysToRemove: string[] = [];
    for (let i = 0; i < localStorage.length; i++) {
      const key = localStorage.key(i);
      if (key && key.startsWith('quote_cache_')) {
        keysToRemove.push(key);
      }
    }
    keysToRemove.forEach(key => localStorage.removeItem(key));

    // Clear market data caches (user-independent but should refresh on new login)
    localStorage.removeItem('ytd_data_cache');
    localStorage.removeItem('cape_data_cache');

    // Keep user preferences (ytd_benchmark_preferences, cape_region_preferences)
    // These are UI preferences and can persist across accounts

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
        // Ignore logout errors
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
