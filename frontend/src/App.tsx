import { BrowserRouter, Routes, Route, Link, Navigate } from 'react-router-dom';
import { useState, useEffect } from 'react';
import { PortfolioPage } from './pages/Portfolio';
import { TransactionsPage } from './pages/Transactions';
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
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-gray-500">Loading...</div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-gray-100">
      <div className="max-w-4xl mx-auto px-4 py-8">
        <h1 className="text-3xl font-bold text-gray-900 mb-8">
          Investment Tracker
        </h1>

        <form onSubmit={handleCreatePortfolio} className="mb-8 flex gap-2">
          <input
            type="text"
            value={newPortfolioName}
            onChange={(e) => setNewPortfolioName(e.target.value)}
            placeholder="New portfolio name..."
            className="flex-1 px-4 py-2 border border-gray-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
          <button
            type="submit"
            disabled={isCreating || !newPortfolioName.trim()}
            className="px-6 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50"
          >
            {isCreating ? 'Creating...' : 'Create'}
          </button>
        </form>

        {portfolios.length === 0 ? (
          <div className="text-center py-12 text-gray-500">
            No portfolios yet. Create one to get started.
          </div>
        ) : (
          <div className="grid gap-4">
            {portfolios.map((portfolio) => (
              <Link
                key={portfolio.id}
                to={`/portfolio/${portfolio.id}`}
                className="block bg-white rounded-lg shadow p-6 hover:shadow-md transition-shadow"
              >
                <h2 className="text-xl font-semibold text-gray-900">
                  {portfolio.name}
                </h2>
                {portfolio.description && (
                  <p className="text-gray-600 mt-1">{portfolio.description}</p>
                )}
                <p className="text-sm text-gray-500 mt-2">
                  {portfolio.baseCurrency} → {portfolio.homeCurrency}
                </p>
              </Link>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<HomePage />} />
        <Route path="/portfolio/:id" element={<PortfolioPage />} />
        <Route path="/portfolio/:portfolioId/transactions" element={<TransactionsPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  );
}

export default App;
