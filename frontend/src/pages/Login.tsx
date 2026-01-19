/**
 * Login Page
 *
 * 提供使用者登入/註冊入口，並在成功後導回先前嘗試進入的頁面。
 *
 * 行為重點：
 * - `isLogin=true`：呼叫 `useAuth().login`
 * - `isLogin=false`：先檢查顯示名稱，再呼叫 `useAuth().register`
 * - 成功後使用 `navigate(from, { replace: true })` 導頁
 */
import { useState } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../hooks/useAuth';

export default function Login() {
  const [isLogin, setIsLogin] = useState(true);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [error, setError] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  const { login, register } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  // React Router 的 location.state 可能包含 ProtectedRoute 傳入的原始目標路徑。
  // 這裡保守地處理型別與缺值，並且避免導回以 `/portfolio` 開頭的頁面（會改導回 `/`）。
  const rawFrom = (location.state as { from?: { pathname: string } })?.from?.pathname || '/';
  const from = rawFrom.startsWith('/portfolio') ? '/' : rawFrom;

  /**
   * 表單送出處理：依模式登入或註冊。
   * @param e React 表單事件
   */
  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setIsSubmitting(true);

    try {
      if (isLogin) {
        await login({ email, password });
      } else {
        if (!displayName.trim()) {
          setError('請輸入顯示名稱');
          setIsSubmitting(false);
          return;
        }
        await register({ email, password, displayName: displayName.trim() });
      }
      navigate(from, { replace: true });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'An error occurred');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="min-h-screen flex items-center justify-center px-4">
      <div className="card-dark max-w-md w-full p-8">
        <h1 className="text-2xl font-bold text-center mb-6">
          <span className="brand-text">Note Wealth</span>
        </h1>

        <div className="flex mb-6">
          <button
            type="button"
            className={`flex-1 py-3 text-center text-base font-medium border-b-2 transition-colors ${
              isLogin
                ? 'border-[var(--accent-peach)] text-[var(--accent-peach)]'
                : 'border-[var(--border-color)] text-[var(--text-muted)] hover:text-[var(--text-secondary)]'
            }`}
            onClick={() => setIsLogin(true)}
          >
            登入
          </button>
          <button
            type="button"
            className={`flex-1 py-3 text-center text-base font-medium border-b-2 transition-colors ${
              !isLogin
                ? 'border-[var(--accent-peach)] text-[var(--accent-peach)]'
                : 'border-[var(--border-color)] text-[var(--text-muted)] hover:text-[var(--text-secondary)]'
            }`}
            onClick={() => setIsLogin(false)}
          >
            註冊
          </button>
        </div>

        {error && (
          <div className="mb-4 p-3 bg-[var(--color-danger-soft)] border border-[var(--color-danger)] rounded-lg text-[var(--color-danger)] text-base">
            {error}
          </div>
        )}

        <form onSubmit={handleSubmit} className="space-y-5">
          {!isLogin && (
            <div>
              <label htmlFor="displayName" className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                顯示名稱
              </label>
              <input
                id="displayName"
                type="text"
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
                className="input-dark w-full"
                placeholder="請輸入您的名稱"
                required={!isLogin}
              />
            </div>
          )}

          <div>
            <label htmlFor="email" className="block text-base font-medium text-[var(--text-secondary)] mb-2">
              電子郵件
            </label>
            <input
              id="email"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className="input-dark w-full"
              placeholder="請輸入電子郵件"
              required
            />
          </div>

          <div>
            <label htmlFor="password" className="block text-base font-medium text-[var(--text-secondary)] mb-2">
              密碼
            </label>
            <input
              id="password"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="input-dark w-full"
              placeholder="請輸入密碼"
              required
              minLength={6}
            />
            {!isLogin && (
              <p className="mt-2 text-sm text-[var(--text-muted)]">密碼至少 6 個字元</p>
            )}
          </div>

          <button
            type="submit"
            disabled={isSubmitting}
            className="btn-accent w-full py-3 disabled:opacity-50"
          >
            {isSubmitting ? '請稍候...' : isLogin ? '登入' : '建立帳號'}
          </button>
        </form>
      </div>
    </div>
  );
}
