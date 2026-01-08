import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { useEffect, useState } from 'react';
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
import './index.css';

function HomePage() {
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const ensurePortfolio = async () => {
      try {
        setIsLoading(true);
        setError(null);

        const portfolios = await portfolioApi.getAll();
        if (portfolios.length > 0) {
          window.location.href = `/portfolio/${portfolios[0].id}`;
          return;
        }

        const created = await portfolioApi.create({
          baseCurrency: 'USD',
          homeCurrency: 'TWD',
        });

        window.location.href = `/portfolio/${created.id}`;
      } catch (err) {
        setError(err instanceof Error ? err.message : '載入投資組合失敗');
      } finally {
        setIsLoading(false);
      }
    };

    ensurePortfolio();
  }, []);

  if (isLoading) {
    return <PageLoader text="準備投資組合中..." />;
  }

  return (
    <div className="max-w-4xl mx-auto px-4 py-8">
      <h1 className="text-2xl font-bold text-[var(--text-primary)] mb-6">
        我的投資組合
      </h1>
      <div className="card-dark p-6">
        <p className="text-base text-[var(--text-muted)]">
          {error ?? '找不到投資組合，請重新整理。'}
        </p>
      </div>
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
