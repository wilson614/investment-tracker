# Feature Backlog

## Pending Items

### Dashboard & Portfolio Switching Overhaul

**Priority**: Next after 009

**Problem Statement**:
1. Dashboard and Performance pages currently rely on portfolio selection made on the Portfolio page. There is no portfolio dropdown on these pages themselves — users must navigate to the Portfolio page to switch.
2. Dashboard (`/dashboard`) shows data for a single portfolio only. It should display an aggregate summary across all portfolios.
3. Performance Analysis (`/performance`) shows data for a single portfolio only. It should support both individual portfolio selection and an "All Portfolios" aggregate view.

**Current State**:
- `PortfolioSelector` component exists only on `Portfolio.tsx:584`
- Dashboard fetches single portfolio via `portfolioApi.getById(currentPortfolioId)` (`Dashboard.tsx:208`)
- Performance fetches single portfolio data (`Performance.tsx:426`)
- Total Assets page (`/assets`) already has cross-portfolio aggregation at backend level (`GetTotalAssetsSummaryUseCase`), but is a separate page

**Scope**:
- Add portfolio selector (with "All" option) to Dashboard and Performance pages
- Dashboard: default to aggregate view showing all portfolios combined
- Performance: allow switching between individual portfolios and aggregate
- May need new backend APIs for aggregated dashboard/performance data
- Consider reusing or extending the existing Total Assets backend logic

**Estimated Complexity**: 🔴 High (frontend + backend, new APIs, UI flow changes)
