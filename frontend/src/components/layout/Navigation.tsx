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
      className={`flex items-center gap-2 px-3 py-2 rounded-md text-sm font-medium transition-colors ${
        isActive
          ? 'bg-blue-100 text-blue-700'
          : 'text-gray-600 hover:bg-gray-100 hover:text-gray-900'
      }`}
    >
      {Icon && <Icon className="w-4 h-4" />}
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
      className={`flex items-center gap-3 px-4 py-3 text-base font-medium border-l-4 transition-colors ${
        isActive
          ? 'border-blue-500 bg-blue-50 text-blue-700'
          : 'border-transparent text-gray-600 hover:bg-gray-50 hover:border-gray-300 hover:text-gray-900'
      }`}
    >
      {Icon && <Icon className="w-5 h-5" />}
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
    <header className="bg-white shadow-sm sticky top-0 z-50">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="flex justify-between items-center h-16">
          {/* Logo and Desktop Navigation */}
          <div className="flex items-center">
            <Link
              to="/"
              className="flex items-center gap-2 flex-shrink-0 text-xl font-bold text-gray-900 hover:text-blue-600 transition-colors"
            >
              <TrendingUp className="w-6 h-6 text-blue-600" />
              投資追蹤
            </Link>

            {/* Desktop Navigation */}
            <nav className="hidden md:flex md:ml-8 md:space-x-2">
              {navLinks.map((link) => (
                <NavLink key={link.to} to={link.to} icon={link.icon}>
                  {link.label}
                </NavLink>
              ))}
            </nav>
          </div>

          {/* Desktop User Menu */}
          <div className="hidden md:flex md:items-center md:space-x-4">
            <span className="text-sm text-gray-500">
              歡迎, <span className="font-medium text-gray-700">{user?.displayName}</span>
            </span>
            <button
              onClick={handleLogout}
              className="flex items-center gap-2 px-3 py-2 text-sm text-gray-500 hover:text-gray-700 hover:bg-gray-100 rounded-md transition-colors"
            >
              <LogOut className="w-4 h-4" />
              登出
            </button>
          </div>

          {/* Mobile menu button */}
          <div className="flex items-center md:hidden">
            <button
              onClick={() => setIsMobileMenuOpen(!isMobileMenuOpen)}
              className="inline-flex items-center justify-center p-2 rounded-md text-gray-500 hover:text-gray-700 hover:bg-gray-100 focus:outline-none focus:ring-2 focus:ring-inset focus:ring-blue-500"
              aria-expanded={isMobileMenuOpen}
              aria-label="Toggle navigation menu"
            >
              {isMobileMenuOpen ? (
                <X className="h-6 w-6" />
              ) : (
                <Menu className="h-6 w-6" />
              )}
            </button>
          </div>
        </div>
      </div>

      {/* Mobile menu */}
      <div
        className={`md:hidden transition-all duration-200 ease-in-out ${
          isMobileMenuOpen
            ? 'max-h-96 opacity-100'
            : 'max-h-0 opacity-0 overflow-hidden'
        }`}
      >
        <div className="border-t border-gray-200 bg-white">
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
          <div className="border-t border-gray-200 py-4 px-4">
            <div className="flex items-center justify-between">
              <div className="text-sm text-gray-600">
                歡迎, <span className="font-medium">{user?.displayName}</span>
              </div>
              <button
                onClick={handleLogout}
                className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-red-600 hover:text-red-700 hover:bg-red-50 rounded-md transition-colors"
              >
                <LogOut className="w-4 h-4" />
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
