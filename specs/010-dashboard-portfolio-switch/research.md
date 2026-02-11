# Research: Dashboard & Portfolio Switching Overhaul

**Feature**: 010-dashboard-portfolio-switch
**Date**: 2026-02-11

## Decision 1: Aggregation Strategy (Frontend vs Backend)

**Decision**: Hybrid approach — frontend aggregation for simple data merging, new backend endpoints only for mathematically complex calculations (XIRR, TWR).

**Rationale**:
- This is a personal app with typically 2-5 portfolios. N+1 API calls (6-15 total) are acceptable.
- Constitution mandates "Accuracy First" — XIRR and TWR calculations are mathematically non-trivial and already implemented only on the backend. Frontend reimplementation risks precision errors.
- Summary totals, monthly net worth sums, transaction merging, and position merging are trivial operations safe for frontend aggregation.
- Minimizes backend scope while ensuring calculation accuracy.

**Alternatives considered**:
1. **Pure frontend aggregation**: Would require porting XIRR/TWR calculations to JS or skipping them. Rejected due to accuracy requirements.
2. **Full backend aggregate API** (single `/api/dashboard/aggregate`): Cleaner but significantly larger backend scope. Rejected for MVP; can be optimized later if needed.
3. **Pure per-portfolio calls with no new endpoints**: Would omit aggregate XIRR and aggregate TWR. Rejected because these are the most valuable aggregate metrics.

## Decision 2: "All Portfolios" Selection State Representation

**Decision**: Use a sentinel string value `"all"` for `currentPortfolioId` in PortfolioContext.

**Rationale**:
- Minimal type changes — `currentPortfolioId` stays `string | null` (just `"all"` is a new valid value).
- localStorage persistence works identically (store `"all"` as a string).
- Easy conditional checks: `currentPortfolioId === 'all'` throughout the codebase.
- `currentPortfolio` (the Portfolio object) is `null` when "all" is selected, with a new `isAllPortfolios` derived boolean for clarity.

**Alternatives considered**:
1. **Use `null` to mean "all"**: Currently `null` means "no portfolio" (loading/empty state). Overloading would create ambiguity.
2. **Separate `isAllPortfolios: boolean` flag**: Adds a second state variable to keep in sync. More error-prone.
3. **Union type `string | 'all' | null`**: Functionally same as sentinel but adds type complexity.

## Decision 3: Default Portfolio Selection

**Decision**: Default to `"all"` when no prior selection exists in localStorage.

**Rationale**:
- Spec FR-004 requires "All Portfolios" as default.
- Currently defaults to first portfolio. Changing default to `"all"` provides immediate value — users see their whole investment picture on first visit.
- Breaking change from current behavior is intentional and desired.

## Decision 4: Portfolio Page Behavior with "All" Selected

**Decision**: When navigating to Portfolio management page with "all" selected, auto-select the first portfolio.

**Rationale**:
- Portfolio page requires a specific portfolio for CRUD operations (add transaction, edit holdings).
- Auto-selecting first portfolio is the least disruptive fallback.
- Selection changes on Portfolio page propagate back to Dashboard/Performance as expected.

## Decision 5: New Backend Endpoints

**Decision**: Create 3 new backend endpoints under existing controller structure.

| Endpoint | Purpose | Why Backend Required |
|----------|---------|---------------------|
| `POST /api/portfolios/aggregate/xirr` | Aggregate XIRR across all portfolios | XIRR uses Newton-Raphson solver, only implemented on backend |
| `POST /api/portfolios/aggregate/performance/year` | Aggregate annual performance (MWR, TWR) | TWR requires sub-period portfolio valuations; Modified Dietz uses weighted cash flows |
| `GET /api/portfolios/aggregate/performance/years` | Union of available years across all portfolios | Simple but avoids N API calls |

**Frontend-aggregated data** (no new backend endpoints):
- Portfolio summary totals: sum `totalCostHome`, `totalValueHome` from per-portfolio summaries
- Monthly net worth: sum `value` per month across per-portfolio monthly data
- Asset allocation: merge positions by ticker
- Top performers: merge all positions, rank by return
- Recent transactions: merge from all portfolios, sort by date, take top 5
- Per-portfolio breakdown: individual summaries displayed in a breakdown section

## Decision 6: Dashboard Quote Cache in Aggregate Mode

**Decision**: In aggregate mode, "Fetch All Prices" fetches prices for all unique tickers across all portfolios, using the same per-ticker cache keys.

**Rationale**:
- Same ticker in multiple portfolios has the same current price.
- Cache key format should be unified (currently differs between Dashboard and Performance pages — `quote_cache_${ticker}_${market}` vs `quote_cache_${ticker}`). This is a pre-existing inconsistency that should be normalized.

## Decision 7: Aggregate Modified Dietz Calculation

**Decision**: For aggregate Modified Dietz on Performance page, the backend will combine all transactions from all portfolios and calculate as if they were a single portfolio.

**Rationale**:
- Modified Dietz formula: `(V1 - V0 - CF) / (V0 + Σ(Wi * CFi))`. With aggregate values (sum of V0s, sum of V1s, all CFs combined), this produces the mathematically correct aggregate money-weighted return.
- This is simpler and more accurate than attempting to weight-average individual portfolio Modified Dietz results.
