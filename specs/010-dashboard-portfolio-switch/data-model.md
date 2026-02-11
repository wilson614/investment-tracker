# Data Model: Dashboard & Portfolio Switching Overhaul

**Feature**: 010-dashboard-portfolio-switch
**Date**: 2026-02-11

## No Database Schema Changes

This feature does NOT require new database tables or migrations. All aggregation is computed from existing data.

## Modified Entities (Application Layer Only)

### 1. Portfolio Selection State (Frontend Context)

**Current**: `currentPortfolioId: string | null`
**New**: `currentPortfolioId: string | null` where `"all"` is a valid sentinel value

| Field | Type | Description |
|-------|------|-------------|
| `currentPortfolioId` | `string \| null` | `"all"` = aggregate view, GUID string = specific portfolio, `null` = loading/no portfolios |
| `isAllPortfolios` | `boolean` (derived) | `currentPortfolioId === 'all'` |
| `currentPortfolio` | `Portfolio \| null` | `null` when `isAllPortfolios` is true |
| `portfolios` | `Portfolio[]` | All user portfolios (unchanged) |

**Persistence**: localStorage key `selected_portfolio_id` stores `"all"` or a portfolio GUID.

### 2. Aggregate XIRR Result (New Backend DTO)

Extends existing `XirrResultDto` pattern for multi-portfolio context.

| Field | Type | Description |
|-------|------|-------------|
| `xirr` | `double?` | Aggregate XIRR value |
| `xirrPercentage` | `double?` | Aggregate XIRR as percentage |
| `cashFlowCount` | `int` | Total cash flows across all portfolios |
| `asOfDate` | `DateTime` | Calculation date |
| `earliestTransactionDate` | `DateTime?` | Earliest transaction across all portfolios |
| `missingExchangeRates` | `MissingExchangeRateDto[]?` | Combined missing rates |

### 3. Aggregate Year Performance (New Backend DTO)

Extends existing `YearPerformanceDto` pattern for multi-portfolio context.

| Field | Type | Description |
|-------|------|-------------|
| `year` | `int` | Target year |
| `startValueHome` | `decimal` | Sum of all portfolio start values |
| `endValueHome` | `decimal` | Sum of all portfolio end values |
| `netContributionsHome` | `decimal` | Sum of all portfolio net contributions |
| `xirrPercentage` | `double?` | Aggregate XIRR |
| `modifiedDietzPercentage` | `double?` | Aggregate Modified Dietz return |
| `timeWeightedReturnPercentage` | `double?` | Aggregate TWR |
| `totalReturnPercentage` | `double?` | Aggregate total return |
| `cashFlowCount` | `int` | Combined cash flows |
| `transactionCount` | `int` | Combined transactions |
| `missingPrices` | `MissingPriceDto[]?` | Combined missing prices across portfolios |

### 4. Aggregate Available Years (New Backend DTO)

| Field | Type | Description |
|-------|------|-------------|
| `years` | `int[]` | Union of all portfolio years (descending) |
| `earliestYear` | `int?` | Earliest year across all portfolios |
| `currentYear` | `int` | Current calendar year |

## Relationships

```
User (1) ──── owns ──── (*) Portfolio
                              │
                              ├── has (*) StockTransaction
                              ├── has (*) StockPosition (computed)
                              └── has (*) MonthlyNetWorthSnapshot

Portfolio Selection State ───── references ──── Portfolio | "all"

Aggregate Views (computed, no persistence):
  - Aggregate Summary = Σ(Portfolio Summaries)
  - Aggregate XIRR = f(all transactions, all current values)
  - Aggregate Performance = f(all transactions, all valuations for year)
  - Aggregate Monthly = Σ(Portfolio Monthly Net Worth per month)
```

## Validation Rules

- `currentPortfolioId` must be one of: `"all"`, a valid portfolio GUID owned by the user, or `null` (loading state).
- Aggregate calculations must include ALL active portfolios for the current user.
- If localStorage contains a portfolio ID that no longer exists, fall back to `"all"`.
- Aggregate XIRR requires at least one transaction across all portfolios to compute.
