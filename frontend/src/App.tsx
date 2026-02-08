import { BrowserRouter, Routes, Route, Navigate, useParams } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthProvider } from './hooks/useAuth';
import { PortfolioProvider } from './contexts/PortfolioContext';
import { LedgerProvider } from './contexts/LedgerContext';
import { DashboardPage } from './pages/Dashboard';
import { PortfolioPage } from './pages/Portfolio';
import { PositionDetailPage } from './pages/PositionDetail';
import { TransactionsPage } from './pages/Transactions';
import { BankAccountsPage } from './features/bank-accounts/pages/BankAccountsPage';
import { TotalAssetsDashboard } from './features/total-assets/pages/TotalAssetsDashboard';
import Currency from './pages/Currency';
import CurrencyDetail from './pages/CurrencyDetail';
import Settings from './pages/Settings';
import Login from './pages/Login';
import { PerformancePage } from './pages/Performance';
import { CreditCardsPage } from './pages/CreditCardsPage';
import { ProtectedRoute, ToastProvider, ScrollToTop } from './components/common';
import { Navigation } from './components/layout/Navigation';
import './index.css';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      retry: 1,
    },
  },
});

function AppLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen bg-[var(--bg-primary)]">
      <Navigation />
      {children}
    </div>
  );
}

function LegacyCurrencyDetailRedirect() {
  const { id } = useParams<{ id: string }>();

  if (!id) {
    return <Navigate to="/ledger" replace />;
  }

  return <Navigate to={`/ledger/${id}`} replace />;
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
        element={<Navigate to="/dashboard" replace />}
      />
      <Route
        path="/portfolio"
        element={
          <ProtectedRoute>
            <AppLayout>
              <PortfolioPage />
            </AppLayout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/portfolio/position/:ticker/:market?"
        element={
          <ProtectedRoute>
            <AppLayout>
              <PositionDetailPage />
            </AppLayout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/portfolio/transactions"
        element={
          <ProtectedRoute>
            <AppLayout>
              <TransactionsPage />
            </AppLayout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/bank-accounts"
        element={
          <ProtectedRoute>
            <AppLayout>
              <BankAccountsPage />
            </AppLayout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/credit-cards"
        element={
          <ProtectedRoute>
            <AppLayout>
              <CreditCardsPage />
            </AppLayout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/assets"
        element={
          <ProtectedRoute>
            <AppLayout>
              <TotalAssetsDashboard />
            </AppLayout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/currency"
        element={
          <ProtectedRoute>
            <Navigate to="/ledger" replace />
          </ProtectedRoute>
        }
      />
      <Route
        path="/ledger"
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
            <LegacyCurrencyDetailRedirect />
          </ProtectedRoute>
        }
      />
      <Route
        path="/ledger/:id"
        element={
          <ProtectedRoute>
            <AppLayout>
              <CurrencyDetail />
            </AppLayout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/performance"
        element={
          <ProtectedRoute>
            <AppLayout>
              <PerformancePage />
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
      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
  );
}

function App() {
  return (
    <BrowserRouter>
      <ScrollToTop />
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <LedgerProvider>
            <PortfolioProvider>
              <ToastProvider>
                <AppRoutes />
              </ToastProvider>
            </PortfolioProvider>
          </LedgerProvider>
        </AuthProvider>
      </QueryClientProvider>
    </BrowserRouter>
  );
}

export default App;
