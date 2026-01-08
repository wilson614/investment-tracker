import { BrowserRouter, Routes, Route, Link, Navigate } from 'react-router-dom';
import { useState, useEffect } from 'react';
import { AuthProvider } from './hooks/useAuth';
import { DashboardPage } from './pages/Dashboard';
import { PortfolioPage } from './pages/Portfolio';
import { PositionDetailPage } from './pages/PositionDetail';
import { TransactionsPage } from './pages/Transactions';
import Currency from './pages/Currency';
import CurrencyDetail from './pages/CurrencyDetail';
import Settings from './pages/Settings';
import Login from './pages/Login';
import { ProtectedRoute, PageLoader, ToastProvider } from './components/common';
import { Navigation } from './components/layout/Navigation';
import { portfolioApi } from './services/api';
import type { Portfolio } from './types';
import './index.css';

function HomePage() {
  const [portfolios, setPortfolios] = useState<Portfolio[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [newPortfolioName, setNewPortfolioName] = useState('');
  const [isCreating, setIsCreating] = useState(false);

  useEffect(() => {
    loadPortfolios();
  }, []);

  const loadPortfolios = async () => {
    try {
      const data = await portfolioApi.getAll();
      setPortfolios(data);
    } catch {
      console.error('Failed to load portfolios');
    } finally {
      setIsLoading(false);
    }
  };

  const handleCreatePortfolio = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newPortfolioName.trim()) return;

    setIsCreating(true);
    try {
      await portfolioApi.create({
        name: newPortfolioName.trim(),
        baseCurrency: 'USD',
        homeCurrency: 'TWD',
      });
      setNewPortfolioName('');
      await loadPortfolios();
    } catch {
      console.error('Failed to create portfolio');
    } finally {
      setIsCreating(false);
    }
  };

  if (isLoading) {
    return <PageLoader text="載入投資組合中..." />;
  }

  return (
    <div className="max-w-4xl mx-auto px-4 py-8">
      <h1 className="text-2xl font-bold text-[var(--text-primary)] mb-6">
        我的投資組合
      </h1>

      <form onSubmit={handleCreatePortfolio} className="mb-8 flex gap-2">
        <input
          type="text"
          value={newPortfolioName}
          onChange={(e) => setNewPortfolioName(e.target.value)}
          placeholder="輸入投資組合名稱..."
          className="input-dark flex-1"
        />
        <button
          type="submit"
          disabled={isCreating || !newPortfolioName.trim()}
          className="btn-accent disabled:opacity-50"
        >
          {isCreating ? '建立中...' : '建立'}
        </button>
      </form>

      {portfolios.length === 0 ? (
        <div className="text-center py-12 text-[var(--text-muted)]">
          尚無投資組合，請建立一個開始使用。
        </div>
      ) : (
        <div className="grid gap-4">
          {portfolios.map((portfolio) => (
            <Link
              key={portfolio.id}
              to={`/portfolio/${portfolio.id}`}
              className="card-dark p-6 hover:border-[var(--border-hover)] transition-all"
            >
              <h2 className="text-xl font-semibold text-[var(--text-primary)]">
                {portfolio.name}
              </h2>
              {portfolio.description && (
                <p className="text-[var(--text-secondary)] mt-1">{portfolio.description}</p>
              )}
              <p className="text-sm text-[var(--text-muted)] mt-2">
                {portfolio.baseCurrency} → {portfolio.homeCurrency}
              </p>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}

function AppLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen bg-[var(--bg-primary)]">
      <Navigation />
      {children}
    </div>
  );
}

function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route
        path="/dashboard"
        element={
          <ProtectedRoute>
            <AppLayout>
              <DashboardPage />
            </AppLayout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/"
        element={
          <ProtectedRoute>
            <AppLayout>
              <HomePage />
            </AppLayout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/portfolio/:id"
        element={
          <ProtectedRoute>
            <AppLayout>
              <PortfolioPage />
            </AppLayout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/portfolio/:id/position/:ticker"
        element={
          <ProtectedRoute>
            <AppLayout>
              <PositionDetailPage />
            </AppLayout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/portfolio/:portfolioId/transactions"
        element={
          <ProtectedRoute>
            <AppLayout>
              <TransactionsPage />
            </AppLayout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/currency"
        element={
          <ProtectedRoute>
            <AppLayout>
              <Currency />
            </AppLayout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/currency/:id"
        element={
          <ProtectedRoute>
            <AppLayout>
              <CurrencyDetail />
            </AppLayout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/settings"
        element={
          <ProtectedRoute>
            <AppLayout>
              <Settings />
            </AppLayout>
          </ProtectedRoute>
        }
      />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}

function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <ToastProvider>
          <AppRoutes />
        </ToastProvider>
      </AuthProvider>
    </BrowserRouter>
  );
}

export default App;
