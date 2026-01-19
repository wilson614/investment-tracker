/**
 * ProtectedRoute
 *
 * 路由保護元件：若尚未登入，會導向 `/login`，並把原本目標路由存到 `location.state.from`。
 *
 * 注意：
 * - `useAuth().isLoading` 為 true 時，先顯示 loading 狀態，避免閃爍。
 */
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';

interface ProtectedRouteProps {
  /** 需要被保護的內容 */
  children: React.ReactNode;
}

export default function ProtectedRoute({ children }: ProtectedRouteProps) {
  const { isAuthenticated, isLoading } = useAuth();
  const location = useLocation();

  if (isLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-[var(--bg-primary)]">
        <div className="text-[var(--text-muted)]">載入中...</div>
      </div>
    );
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" state={{ from: location }} replace />;
  }

  return <>{children}</>;
}
