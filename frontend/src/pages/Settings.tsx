/**
 * Settings Page
 *
 * 帳戶設定頁：提供個人資料（displayName/email）更新與密碼變更。
 *
 * 主要互動：
 * - 透過 `authApi.updateProfile` 更新資料，並同步寫回 `useAuth().setUser`。
 * - 透過 `authApi.changePassword` 更新密碼，並做基本表單驗證（確認密碼、最小長度）。
 */
import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowLeft, User, Mail, Lock, Save, TrendingUp } from 'lucide-react';
import { authApi } from '../services/api';
import { useAuth } from '../hooks/useAuth';
import { BenchmarkSettings } from '../components/settings/BenchmarkSettings';

export default function Settings() {
  const navigate = useNavigate();
  const { user, setUser } = useAuth();

  // Profile form
  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [profileLoading, setProfileLoading] = useState(false);
  const [profileError, setProfileError] = useState<string | null>(null);
  const [profileSuccess, setProfileSuccess] = useState(false);

  // Password form
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [passwordLoading, setPasswordLoading] = useState(false);
  const [passwordError, setPasswordError] = useState<string | null>(null);
  const [passwordSuccess, setPasswordSuccess] = useState(false);

  useEffect(() => {
    if (user) {
      setDisplayName(user.displayName);
      setEmail(user.email);
    }
  }, [user]);

  /**
   * 更新個人資料（displayName/email）。
   * @param e React 表單事件
   */
  const handleProfileSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setProfileError(null);
    setProfileSuccess(false);
    setProfileLoading(true);

    try {
      const updated = await authApi.updateProfile({
        displayName: displayName.trim(),
        email: email.trim(),
      });
      setUser({
        ...user!,
        displayName: updated.displayName,
        email: updated.email,
      });
      setProfileSuccess(true);
    } catch (err) {
      setProfileError(err instanceof Error ? err.message : '更新失敗');
    } finally {
      setProfileLoading(false);
    }
  };

  /**
   * 變更密碼。
   *
   * 行內驗證：
   * - 新密碼與確認密碼需一致
   * - 新密碼長度至少 6 個字元
   * @param e React 表單事件
   */
  const handlePasswordSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setPasswordError(null);
    setPasswordSuccess(false);

    if (newPassword !== confirmPassword) {
      setPasswordError('新密碼與確認密碼不符');
      return;
    }

    if (newPassword.length < 6) {
      setPasswordError('新密碼至少需要 6 個字元');
      return;
    }

    setPasswordLoading(true);

    try {
      await authApi.changePassword({
        currentPassword,
        newPassword,
      });
      setPasswordSuccess(true);
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
    } catch (err) {
      setPasswordError(err instanceof Error ? err.message : '密碼更新失敗');
    } finally {
      setPasswordLoading(false);
    }
  };

  return (
    <div className="min-h-screen py-8">
      <div className="max-w-2xl mx-auto px-4 sm:px-6 lg:px-8">
        {/* Back Button */}
        <button
          onClick={() => navigate(-1)}
          className="flex items-center gap-2 text-[var(--text-secondary)] hover:text-[var(--text-primary)] mb-6 text-base transition-colors"
        >
          <ArrowLeft className="w-5 h-5" />
          返回
        </button>

        <h1 className="text-2xl font-bold text-[var(--text-primary)] mb-8">帳戶設定</h1>

        {/* Profile Section */}
        <div className="card-dark p-6 mb-6">
          <h2 className="text-lg font-bold text-[var(--text-primary)] mb-4 flex items-center gap-2">
            <User className="w-5 h-5 text-[var(--accent-peach)]" />
            個人資料
          </h2>

          {profileError && (
            <div className="p-3 bg-[var(--color-danger-soft)] border border-[var(--color-danger)] text-[var(--color-danger)] rounded-lg text-base mb-4">
              {profileError}
            </div>
          )}

          {profileSuccess && (
            <div className="p-3 bg-[var(--color-success-soft)] border border-[var(--color-success)] text-[var(--color-success)] rounded-lg text-base mb-4">
              個人資料已更新
            </div>
          )}

          <form onSubmit={handleProfileSubmit} className="space-y-4">
            <div>
              <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                顯示名稱
              </label>
              <div className="relative">
                <User className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-[var(--text-muted)] pointer-events-none" />
                <input
                  type="text"
                  value={displayName}
                  onChange={(e) => setDisplayName(e.target.value)}
                  className="input-dark w-full pl-12"
                  required
                  maxLength={100}
                />
              </div>
            </div>

            <div>
              <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                電子郵件
              </label>
              <div className="relative">
                <Mail className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-[var(--text-muted)] pointer-events-none" />
                <input
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  className="input-dark w-full pl-12"
                  required
                  maxLength={256}
                />
              </div>
            </div>

            <button
              type="submit"
              disabled={profileLoading}
              className="btn-accent flex items-center justify-center gap-2 w-full py-3 disabled:opacity-50"
            >
              <Save className="w-5 h-5" />
              {profileLoading ? '儲存中...' : '儲存變更'}
            </button>
          </form>
        </div>

        {/* Benchmark Settings Section */}
        <div className="card-dark p-6 mb-6">
          <h2 className="text-lg font-bold text-[var(--text-primary)] mb-4 flex items-center gap-2">
            <TrendingUp className="w-5 h-5 text-[var(--accent-peach)]" />
            自訂基準指數
          </h2>
          <p className="text-sm text-[var(--text-muted)] mb-4">
            新增自訂股票/ETF 作為績效比較基準。這些會顯示在「歷史績效」頁面的績效比較圖表中。
          </p>
          <BenchmarkSettings />
        </div>

        {/* Password Section */}
        <div className="card-dark p-6">
          <h2 className="text-lg font-bold text-[var(--text-primary)] mb-4 flex items-center gap-2">
            <Lock className="w-5 h-5 text-[var(--accent-butter)]" />
            變更密碼
          </h2>

          {passwordError && (
            <div className="p-3 bg-[var(--color-danger-soft)] border border-[var(--color-danger)] text-[var(--color-danger)] rounded-lg text-base mb-4">
              {passwordError}
            </div>
          )}

          {passwordSuccess && (
            <div className="p-3 bg-[var(--color-success-soft)] border border-[var(--color-success)] text-[var(--color-success)] rounded-lg text-base mb-4">
              密碼已更新
            </div>
          )}

          <form onSubmit={handlePasswordSubmit} className="space-y-4">
            <div>
              <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                目前密碼
              </label>
              <input
                type="password"
                value={currentPassword}
                onChange={(e) => setCurrentPassword(e.target.value)}
                className="input-dark w-full"
                required
              />
            </div>

            <div>
              <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                新密碼
              </label>
              <input
                type="password"
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                className="input-dark w-full"
                required
                minLength={6}
              />
            </div>

            <div>
              <label className="block text-base font-medium text-[var(--text-secondary)] mb-2">
                確認新密碼
              </label>
              <input
                type="password"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                className="input-dark w-full"
                required
                minLength={6}
              />
            </div>

            <button
              type="submit"
              disabled={passwordLoading}
              className="btn-dark flex items-center justify-center gap-2 w-full py-3 disabled:opacity-50"
            >
              <Lock className="w-5 h-5" />
              {passwordLoading ? '更新中...' : '變更密碼'}
            </button>
          </form>
        </div>
      </div>
    </div>
  );
}
