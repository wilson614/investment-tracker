import { useState, useRef, useEffect } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';
import { authApi, portfolioApi } from '../../services/api';
import { LayoutDashboard, Briefcase, Wallet, Menu, X, LogOut, TrendingUp, User, ChevronDown, Lock, Mail, Save } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';

interface NavLinkProps {
  to: string;
  children: React.ReactNode;
  icon?: LucideIcon;
  onClick?: () => void;
}

function NavLink({ to, children, icon: Icon, onClick }: NavLinkProps) {
  const location = useLocation();
  const isActive = location.pathname === to ||
    (to !== '/' && location.pathname.startsWith(to));

  return (
    <Link
      to={to}
      onClick={onClick}
      className={`flex items-center gap-2 px-4 py-2.5 rounded-lg text-base font-medium transition-all duration-200 ${
        isActive
          ? 'bg-[var(--accent-peach-soft)] text-[var(--accent-peach)]'
          : 'text-[var(--text-secondary)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)]'
      }`}
    >
      {Icon && <Icon className="w-5 h-5" />}
      {children}
    </Link>
  );
}

function MobileNavLink({ to, children, icon: Icon, onClick }: NavLinkProps) {
  const location = useLocation();
  const isActive = location.pathname === to ||
    (to !== '/' && location.pathname.startsWith(to));

  return (
    <Link
      to={to}
      onClick={onClick}
      className={`flex items-center gap-3 px-5 py-4 text-lg font-medium border-l-4 transition-all duration-200 ${
        isActive
          ? 'border-[var(--accent-peach)] bg-[var(--accent-peach-soft)] text-[var(--accent-peach)]'
          : 'border-transparent text-[var(--text-secondary)] hover:bg-[var(--bg-hover)] hover:border-[var(--border-hover)] hover:text-[var(--text-primary)]'
      }`}
    >
      {Icon && <Icon className="w-6 h-6" />}
      {children}
    </Link>
  );
}

export function Navigation() {
  const { user, logout, setUser } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);
  const [isDropdownOpen, setIsDropdownOpen] = useState(false);
  const [showProfileModal, setShowProfileModal] = useState(false);
  const [showPasswordModal, setShowPasswordModal] = useState(false);
  const [portfolioId, setPortfolioId] = useState<string | null>(null);
  const dropdownRef = useRef<HTMLDivElement>(null);

  // Profile form state
  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [profileLoading, setProfileLoading] = useState(false);
  const [profileError, setProfileError] = useState<string | null>(null);
  const [profileSuccess, setProfileSuccess] = useState(false);

  // Password form state
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [passwordLoading, setPasswordLoading] = useState(false);
  const [passwordError, setPasswordError] = useState<string | null>(null);
  const [passwordSuccess, setPasswordSuccess] = useState(false);

  // Click outside to close dropdown
  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) {
        setIsDropdownOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  // Initialize profile form when modal opens
  useEffect(() => {
    if (showProfileModal && user) {
      setDisplayName(user.displayName);
      setEmail(user.email);
      setProfileError(null);
      setProfileSuccess(false);
    }
  }, [showProfileModal, user]);

  // Reset password form when modal opens
  useEffect(() => {
    if (showPasswordModal) {
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
      setPasswordError(null);
      setPasswordSuccess(false);
    }
  }, [showPasswordModal]);

  // Load portfolio ID on mount
  useEffect(() => {
    const loadPortfolioId = async () => {
      try {
        const portfolios = await portfolioApi.getAll();
        if (portfolios.length > 0) {
          setPortfolioId(portfolios[0].id);
        }
      } catch {
        // Ignore errors
      }
    };
    loadPortfolioId();
  }, []);

  const handlePortfolioClick = async (e: React.MouseEvent) => {
    e.preventDefault();

    let targetId = portfolioId;

    if (!targetId) {
      try {
        const portfolios = await portfolioApi.getAll();
        if (portfolios.length > 0) {
          targetId = portfolios[0].id;
          setPortfolioId(targetId);
        } else {
          navigate('/');
          closeMobileMenu();
          return;
        }
      } catch {
        navigate('/');
        closeMobileMenu();
        return;
      }
    }

    navigate(`/portfolio/${targetId}`);
    closeMobileMenu();
  };

  const handleLogout = async () => {
    await logout();
    setIsMobileMenuOpen(false);
    setIsDropdownOpen(false);
  };

  const closeMobileMenu = () => {
    setIsMobileMenuOpen(false);
  };

  const handleOpenProfile = () => {
    setIsDropdownOpen(false);
    setShowProfileModal(true);
  };

  const handleOpenPassword = () => {
    setIsDropdownOpen(false);
    setShowPasswordModal(true);
  };

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
      setTimeout(() => setShowProfileModal(false), 1000);
    } catch (err) {
      setProfileError(err instanceof Error ? err.message : '更新失敗');
    } finally {
      setProfileLoading(false);
    }
  };

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
      setTimeout(() => setShowPasswordModal(false), 1000);
    } catch (err) {
      setPasswordError(err instanceof Error ? err.message : '密碼更新失敗');
    } finally {
      setPasswordLoading(false);
    }
  };

  return (
    <>
    <header className="bg-[var(--bg-secondary)] border-b border-[var(--border-color)] sticky top-0 z-50">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="flex justify-between items-center h-16">
          {/* Logo and Desktop Navigation */}
          <div className="flex items-center">
            <Link
              to="/dashboard"
              className="flex items-center gap-2 flex-shrink-0 text-xl font-bold transition-colors"
            >
              <TrendingUp className="w-7 h-7 text-[var(--accent-peach)]" />
              <span className="brand-text">投資追蹤</span>
            </Link>

            {/* Desktop Navigation */}
            <nav className="hidden md:flex md:ml-10 md:space-x-2">
              <NavLink to="/dashboard" icon={LayoutDashboard}>儀表板</NavLink>
              <a
                href="#"
                onClick={handlePortfolioClick}
                className={`flex items-center gap-2 px-4 py-2.5 rounded-lg text-base font-medium transition-all duration-200 ${
                  location.pathname.startsWith('/portfolio')
                    ? 'bg-[var(--accent-peach-soft)] text-[var(--accent-peach)]'
                    : 'text-[var(--text-secondary)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)]'
                }`}
              >
                <Briefcase className="w-5 h-5" />
                投資組合
              </a>
              <NavLink to="/currency" icon={Wallet}>外幣帳本</NavLink>
            </nav>
          </div>

          {/* Desktop User Dropdown */}
          <div className="hidden md:flex md:items-center md:space-x-4">
            <div className="relative" ref={dropdownRef}>
              <button
                onClick={() => setIsDropdownOpen(!isDropdownOpen)}
                className="flex items-center gap-2 px-3 py-2 text-base font-medium text-[var(--text-primary)] hover:bg-[var(--bg-hover)] rounded-lg transition-all duration-200"
              >
                <User className="w-5 h-5 text-[var(--text-muted)]" />
                {user?.displayName}
                <ChevronDown className={`w-4 h-4 text-[var(--text-muted)] transition-transform ${isDropdownOpen ? 'rotate-180' : ''}`} />
              </button>

              {/* Dropdown Menu */}
              {isDropdownOpen && (
                <div className="absolute right-0 mt-2 w-48 bg-[var(--bg-card)] border border-[var(--border-color)] rounded-lg shadow-lg py-1 z-50 animate-fade-in">
                  <button
                    onClick={handleOpenProfile}
                    className="flex items-center gap-2 w-full px-4 py-2.5 text-base text-[var(--text-primary)] hover:bg-[var(--bg-hover)] transition-colors"
                  >
                    <User className="w-4 h-4 text-[var(--text-muted)]" />
                    個人資料
                  </button>
                  <button
                    onClick={handleOpenPassword}
                    className="flex items-center gap-2 w-full px-4 py-2.5 text-base text-[var(--text-primary)] hover:bg-[var(--bg-hover)] transition-colors"
                  >
                    <Lock className="w-4 h-4 text-[var(--text-muted)]" />
                    變更密碼
                  </button>
                  <div className="border-t border-[var(--border-color)] my-1" />
                  <button
                    onClick={handleLogout}
                    className="flex items-center gap-2 w-full px-4 py-2.5 text-base text-[var(--color-danger)] hover:bg-[var(--color-danger-soft)] transition-colors"
                  >
                    <LogOut className="w-4 h-4" />
                    登出
                  </button>
                </div>
              )}
            </div>
          </div>

          {/* Mobile menu button */}
          <div className="flex items-center md:hidden">
            <button
              onClick={() => setIsMobileMenuOpen(!isMobileMenuOpen)}
              className="inline-flex items-center justify-center p-2 rounded-lg text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-[var(--bg-hover)] transition-colors"
              aria-expanded={isMobileMenuOpen}
              aria-label="Toggle navigation menu"
            >
              {isMobileMenuOpen ? (
                <X className="h-7 w-7" />
              ) : (
                <Menu className="h-7 w-7" />
              )}
            </button>
          </div>
        </div>
      </div>

      {/* Mobile menu */}
      <div
        className={`md:hidden transition-all duration-300 ease-in-out ${
          isMobileMenuOpen
            ? 'max-h-[500px] opacity-100'
            : 'max-h-0 opacity-0 overflow-hidden'
        }`}
      >
        <div className="border-t border-[var(--border-color)] bg-[var(--bg-secondary)]">
          <nav className="py-2">
            <MobileNavLink to="/dashboard" icon={LayoutDashboard} onClick={closeMobileMenu}>
              儀表板
            </MobileNavLink>
            <a
              href="#"
              onClick={handlePortfolioClick}
              className={`flex items-center gap-3 px-5 py-4 text-lg font-medium border-l-4 transition-all duration-200 ${
                location.pathname.startsWith('/portfolio')
                  ? 'border-[var(--accent-peach)] bg-[var(--accent-peach-soft)] text-[var(--accent-peach)]'
                  : 'border-transparent text-[var(--text-secondary)] hover:bg-[var(--bg-hover)] hover:border-[var(--border-hover)] hover:text-[var(--text-primary)]'
              }`}
            >
              <Briefcase className="w-6 h-6" />
              投資組合
            </a>
            <MobileNavLink to="/currency" icon={Wallet} onClick={closeMobileMenu}>
              外幣帳本
            </MobileNavLink>
          </nav>

          {/* Mobile User Section */}
          <div className="border-t border-[var(--border-color)] py-4 px-5">
            <div className="flex flex-col gap-2">
              <div className="flex items-center gap-2 text-base font-medium text-[var(--text-primary)] mb-2">
                <User className="w-5 h-5 text-[var(--text-muted)]" />
                {user?.displayName}
              </div>
              <button
                onClick={() => { handleOpenProfile(); closeMobileMenu(); }}
                className="flex items-center gap-2 px-3 py-2 text-base text-[var(--text-primary)] hover:bg-[var(--bg-hover)] rounded-lg transition-colors"
              >
                <User className="w-4 h-4 text-[var(--text-muted)]" />
                個人資料
              </button>
              <button
                onClick={() => { handleOpenPassword(); closeMobileMenu(); }}
                className="flex items-center gap-2 px-3 py-2 text-base text-[var(--text-primary)] hover:bg-[var(--bg-hover)] rounded-lg transition-colors"
              >
                <Lock className="w-4 h-4 text-[var(--text-muted)]" />
                變更密碼
              </button>
              <button
                onClick={handleLogout}
                className="flex items-center gap-2 px-3 py-2 text-base font-medium text-[var(--color-danger)] hover:bg-[var(--color-danger-soft)] rounded-lg transition-colors"
              >
                <LogOut className="w-5 h-5" />
                登出
              </button>
            </div>
          </div>
        </div>
      </div>
    </header>

      {/* Profile Modal */}
      {showProfileModal && (
        <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50">
          <div className="card-dark p-6 w-full max-w-md m-4">
            <h2 className="text-xl font-bold text-[var(--text-primary)] mb-4 flex items-center gap-2">
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

              <div className="flex gap-3 pt-2">
                <button
                  type="button"
                  onClick={() => setShowProfileModal(false)}
                  className="btn-dark flex-1 py-2.5"
                >
                  取消
                </button>
                <button
                  type="submit"
                  disabled={profileLoading}
                  className="btn-accent flex items-center justify-center gap-2 flex-1 py-2.5 disabled:opacity-50"
                >
                  <Save className="w-4 h-4" />
                  {profileLoading ? '儲存中...' : '儲存'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Password Modal */}
      {showPasswordModal && (
        <div className="fixed inset-0 modal-overlay flex items-center justify-center z-50">
          <div className="card-dark p-6 w-full max-w-md m-4">
            <h2 className="text-xl font-bold text-[var(--text-primary)] mb-4 flex items-center gap-2">
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

              <div className="flex gap-3 pt-2">
                <button
                  type="button"
                  onClick={() => setShowPasswordModal(false)}
                  className="btn-dark flex-1 py-2.5"
                >
                  取消
                </button>
                <button
                  type="submit"
                  disabled={passwordLoading}
                  className="btn-accent flex items-center justify-center gap-2 flex-1 py-2.5 disabled:opacity-50"
                >
                  <Lock className="w-4 h-4" />
                  {passwordLoading ? '更新中...' : '變更密碼'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </>
  );
}

export default Navigation;
