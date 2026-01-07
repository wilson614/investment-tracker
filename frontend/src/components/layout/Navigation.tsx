import { useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';
import { LayoutDashboard, Briefcase, Wallet, Menu, X, LogOut, TrendingUp } from 'lucide-react';
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
  const { user, logout } = useAuth();
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);

  const handleLogout = async () => {
    await logout();
    setIsMobileMenuOpen(false);
  };

  const closeMobileMenu = () => {
    setIsMobileMenuOpen(false);
  };

  const navLinks = [
    { to: '/dashboard', label: '儀表板', icon: LayoutDashboard },
    { to: '/', label: '投資組合', icon: Briefcase },
    { to: '/currency', label: '外幣帳本', icon: Wallet },
  ];

  return (
    <header className="bg-[var(--bg-secondary)] border-b border-[var(--border-color)] sticky top-0 z-50">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="flex justify-between items-center h-16">
          {/* Logo and Desktop Navigation */}
          <div className="flex items-center">
            <Link
              to="/"
              className="flex items-center gap-2 flex-shrink-0 text-xl font-bold transition-colors"
            >
              <TrendingUp className="w-7 h-7 text-[var(--accent-peach)]" />
              <span className="brand-text">投資追蹤</span>
            </Link>

            {/* Desktop Navigation */}
            <nav className="hidden md:flex md:ml-10 md:space-x-2">
              {navLinks.map((link) => (
                <NavLink key={link.to} to={link.to} icon={link.icon}>
                  {link.label}
                </NavLink>
              ))}
            </nav>
          </div>

          {/* Desktop User Menu */}
          <div className="hidden md:flex md:items-center md:space-x-4">
            <span className="text-base text-[var(--text-muted)]">
              歡迎, <span className="font-medium text-[var(--text-primary)]">{user?.displayName}</span>
            </span>
            <button
              onClick={handleLogout}
              className="flex items-center gap-2 px-4 py-2 text-base text-[var(--text-muted)] hover:text-[var(--color-danger)] hover:bg-[var(--color-danger-soft)] rounded-lg transition-all duration-200"
            >
              <LogOut className="w-5 h-5" />
              登出
            </button>
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
            {navLinks.map((link) => (
              <MobileNavLink
                key={link.to}
                to={link.to}
                icon={link.icon}
                onClick={closeMobileMenu}
              >
                {link.label}
              </MobileNavLink>
            ))}
          </nav>

          {/* Mobile User Section */}
          <div className="border-t border-[var(--border-color)] py-4 px-5">
            <div className="flex items-center justify-between">
              <div className="text-base text-[var(--text-secondary)]">
                歡迎, <span className="font-medium text-[var(--text-primary)]">{user?.displayName}</span>
              </div>
              <button
                onClick={handleLogout}
                className="flex items-center gap-2 px-4 py-2 text-base font-medium text-[var(--color-danger)] hover:bg-[var(--color-danger-soft)] rounded-lg transition-colors"
              >
                <LogOut className="w-5 h-5" />
                登出
              </button>
            </div>
          </div>
        </div>
      </div>
    </header>
  );
}

export default Navigation;
