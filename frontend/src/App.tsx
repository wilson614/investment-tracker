import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider } from './hooks/useAuth';
import { PortfolioProvider } from './contexts/PortfolioContext';
import { DashboardPage } from './pages/Dashboard';
import { PortfolioPage } from './pages/Portfolio';
import { PositionDetailPage } from './pages/PositionDetail';
import { TransactionsPage } from './pages/Transactions';
import Currency from './pages/Currency';
import CurrencyDetail from './pages/CurrencyDetail';
import Settings from './pages/Settings';
import Login from './pages/Login';
import { PerformancePage } from './pages/Performance';
import { ProtectedRoute, ToastProvider, ScrollToTop } from './components/common';
import { Navigation } from './components/layout/Navigation';
import './index.css';

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
      <AuthProvider>
        <PortfolioProvider>
          <ToastProvider>
            <AppRoutes />
          </ToastProvider>
        </PortfolioProvider>
      </AuthProvider>
    </BrowserRouter>
  );
}

export default App;
