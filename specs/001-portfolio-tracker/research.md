# Research: Family Investment Portfolio Tracker

**Feature**: 001-portfolio-tracker
**Date**: 2026-01-09
**Status**: Complete (Updated for Page Refresh Behavior, CSV Export, Market YTD Comparison)

## Executive Summary

This document captures technical research and architectural decisions for the investment
portfolio tracker. All major decisions are resolved; no outstanding clarifications remain.

---

## 1. XIRR Calculation Algorithm

### Decision
Use **Newton-Raphson method** with Brent's method fallback for XIRR calculation.

### Rationale
- Newton-Raphson is the standard approach used by Excel's XIRR function
- Fast convergence (typically 10-20 iterations for financial data)
- Well-documented edge case handling
- Brent's method as fallback handles cases where Newton-Raphson fails to converge

### Implementation Details
```csharp
// Domain Service: PortfolioCalculator.cs
public decimal CalculateXirr(IEnumerable<CashFlow> cashFlows, decimal guess = 0.1m)
{
    // Newton-Raphson with max 100 iterations
    // Tolerance: 1e-7 (matches Excel's 0.01% requirement from SC-002)
    // Fallback to Brent's method if no convergence
}
```

### Alternatives Considered
| Alternative | Why Rejected |
|-------------|--------------|
| Bisection method | Slower convergence, more iterations needed |
| Secant method | Less stable for edge cases |
| External library (Excel Interop) | Adds Windows dependency, violates self-hosted principle |

### References
- Excel XIRR documentation
- Numerical Recipes: Newton-Raphson implementation

---

## 2. Weighted Average Cost Calculation

### Decision
Implement **moving weighted average** recalculated from T=0 on any transaction change.

### Rationale
- User chose "editable history" in clarification session
- Family-scale data (thousands of records) makes full recalculation feasible
- Simpler logic than incremental updates with rollback
- Guarantees consistency after any edit

### Implementation Details
```csharp
// Domain Service: PortfolioCalculator.cs
public PositionSummary RecalculatePosition(Guid portfolioId, string ticker)
{
    var transactions = _repository.GetTransactions(portfolioId, ticker)
                                  .OrderBy(t => t.Date);

    decimal totalShares = 0;
    decimal totalCostHome = 0;

    foreach (var tx in transactions)
    {
        if (tx.Type == TransactionType.Buy)
        {
            totalShares += tx.Shares;
            totalCostHome += tx.TotalCostHome;
        }
        else // Sell
        {
            var avgCost = totalCostHome / totalShares;
            totalShares -= tx.Shares;
            totalCostHome -= tx.Shares * avgCost;
        }
    }

    return new PositionSummary(totalShares, totalCostHome);
}
```

### Alternatives Considered
| Alternative | Why Rejected |
|-------------|--------------|
| FIFO (First-In-First-Out) | User explicitly chose average cost method |
| Specific lot identification | Too complex for family use case |
| Incremental-only updates | Cannot support transaction editing |

---

## 3. Currency Ledger Weighted Average Rate

### Decision
Same approach as stock positions: **recalculate from T=0** on any change.

### Rationale
- Consistency with stock transaction approach
- Interest transactions with 0 cost basis naturally handled
- Spend transactions correctly consume at current average (no rate change)

### Implementation Details
```csharp
// Domain Service: CurrencyLedgerService.cs
public CurrencyLedgerSummary RecalculateLedger(Guid ledgerId)
{
    var transactions = _repository.GetCurrencyTransactions(ledgerId)
                                  .OrderBy(t => t.Date);

    decimal balance = 0;
    decimal totalCostHome = 0;

    foreach (var tx in transactions)
    {
        switch (tx.Type)
        {
            case CurrencyTransactionType.ExchangeBuy:
                balance += tx.ForeignAmount;
                totalCostHome += tx.HomeAmount;
                break;
            case CurrencyTransactionType.ExchangeSell:
            case CurrencyTransactionType.Spend:
                var avgRate = totalCostHome / balance;
                balance -= tx.ForeignAmount;
                totalCostHome -= tx.ForeignAmount * avgRate;
                break;
            case CurrencyTransactionType.Interest:
                balance += tx.ForeignAmount;
                // Cost basis = 0 (or market rate if configured)
                break;
        }
    }

    return new CurrencyLedgerSummary(balance, totalCostHome / balance);
}
```

---

## 4. Multi-Tenancy Implementation

### Decision
**Row-level filtering** with UserId foreign key on all tenant-scoped entities.

### Rationale
- Simple to implement with EF Core global query filters
- Minimal performance overhead
- Easy to audit and test
- Suitable for family-scale (10 users)

### Implementation Details
```csharp
// Infrastructure: AppDbContext.cs
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // Global query filter for all tenant-scoped entities
    modelBuilder.Entity<Portfolio>()
        .HasQueryFilter(p => p.UserId == _currentUserService.UserId);

    modelBuilder.Entity<StockTransaction>()
        .HasQueryFilter(t => t.Portfolio.UserId == _currentUserService.UserId);

    // ... similar for CurrencyLedger, CurrencyTransaction
}
```

### Alternatives Considered
| Alternative | Why Rejected |
|-------------|--------------|
| Separate schemas per user | Overkill for 10 users, complex migrations |
| Separate databases | Unnecessary isolation, operational overhead |
| Application-level filtering only | Error-prone, no DB-level guarantee |

---

## 5. Atomic Transaction for Stock+Currency Integration

### Decision
Use **EF Core database transaction** wrapping both operations.

### Rationale
- User explicitly chose atomic approach in clarification
- PostgreSQL supports ACID transactions
- EF Core's `SaveChangesAsync()` within `TransactionScope` ensures atomicity

### Implementation Details
```csharp
// Application: CreateStockTransactionUseCase.cs
public async Task<Result> Execute(CreateStockTransactionRequest request)
{
    await using var transaction = await _dbContext.Database.BeginTransactionAsync();
    try
    {
        // 1. Create stock transaction
        var stockTx = new StockTransaction(...);
        _dbContext.StockTransactions.Add(stockTx);

        // 2. If using currency ledger, create spend transaction
        if (request.FundSource == FundSource.CurrencyLedger)
        {
            var spendTx = new CurrencyTransaction(
                type: CurrencyTransactionType.Spend,
                foreignAmount: stockTx.TotalCostSource,
                ...);
            _dbContext.CurrencyTransactions.Add(spendTx);
        }

        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        // 3. Trigger recalculation
        await _portfolioCalculator.RecalculatePosition(...);

        return Result.Success();
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}
```

---

## 6. JWT Authentication

### Decision
**ASP.NET Core JWT Bearer** with refresh token rotation.

### Rationale
- Native .NET 8 support, no external dependencies
- Stateless authentication suitable for API
- Refresh tokens stored in database for revocation capability

### Implementation Details
- Access token: 15 minutes expiry
- Refresh token: 7 days expiry, stored in DB with `is_revoked` flag
- Password hashing: Argon2id (via `Konscious.Security.Cryptography`)

### Configuration
```json
{
  "Jwt": {
    "Secret": "${JWT_SECRET}",
    "Issuer": "InvestmentTracker",
    "Audience": "InvestmentTracker",
    "AccessTokenExpirationMinutes": 15,
    "RefreshTokenExpirationDays": 7
  }
}
```

---

## 7. Decimal Precision for Financial Data

### Decision
Use `decimal(18,4)` for shares, `decimal(18,6)` for exchange rates, `decimal(18,2)` for monetary amounts.

### Rationale
- `decimal` in C# maps to PostgreSQL `numeric`, avoiding floating-point errors
- 4 decimal places for shares matches FR-001 requirement (6.8267)
- 6 decimal places for exchange rates preserves precision
- 2 decimal places for final monetary display

### EF Core Configuration
```csharp
modelBuilder.Entity<StockTransaction>()
    .Property(t => t.Shares)
    .HasPrecision(18, 4);

modelBuilder.Entity<StockTransaction>()
    .Property(t => t.ExchangeRate)
    .HasPrecision(18, 6);

modelBuilder.Entity<StockTransaction>()
    .Property(t => t.PricePerShare)
    .HasPrecision(18, 4);
```

---

## 8. Docker Deployment Architecture

### Decision
Three-container setup: **backend**, **frontend**, **postgres**.

### Rationale
- Separation allows independent scaling/updates
- Frontend served via nginx for production builds
- Backend uses multi-stage build for smaller images

### docker-compose.yml Structure
```yaml
services:
  postgres:
    image: postgres:16-alpine
    volumes:
      - pgdata:/var/lib/postgresql/data
    environment:
      - POSTGRES_DB=investmenttracker
      - POSTGRES_USER=${DB_USER}
      - POSTGRES_PASSWORD=${DB_PASSWORD}

  backend:
    build:
      context: ./backend
      dockerfile: Dockerfile
    depends_on:
      - postgres
    environment:
      - ConnectionStrings__DefaultConnection=...
      - Jwt__Secret=${JWT_SECRET}

  frontend:
    build:
      context: ./frontend
      dockerfile: Dockerfile
    depends_on:
      - backend
    ports:
      - "80:80"

volumes:
  pgdata:
```

---

## 9. API Versioning Strategy

### Decision
**URL path versioning** with `/api/v1/` prefix.

### Rationale
- Explicit and clear for API consumers
- Easy to implement with ASP.NET Core routing
- No header manipulation needed
- Self-documenting URLs

### Implementation
```csharp
[ApiController]
[Route("api/v1/[controller]")]
public class PortfoliosController : ControllerBase
{
    // ...
}
```

---

## 10. Frontend State Management

### Decision
**React Query (TanStack Query)** for server state, React Context for UI state.

### Rationale
- React Query handles caching, refetching, optimistic updates
- Reduces boilerplate compared to Redux
- Built-in support for pagination and infinite queries
- Minimal bundle size impact

### Usage Pattern
```typescript
// hooks/usePortfolio.ts
export function usePortfolio(portfolioId: string) {
  return useQuery({
    queryKey: ['portfolio', portfolioId],
    queryFn: () => api.getPortfolio(portfolioId),
    staleTime: 5000, // 5 seconds
  });
}

export function useCreateTransaction() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: api.createTransaction,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['portfolio'] });
    },
  });
}
```

---

## Summary

| Topic | Decision |
|-------|----------|
| XIRR Algorithm | Newton-Raphson with Brent's fallback |
| Cost Calculation | Moving average, recalculate from T=0 |
| Multi-Tenancy | Row-level filtering with EF Core global filters |
| Transaction Atomicity | EF Core database transactions |
| Authentication | JWT Bearer with refresh token rotation |
| Decimal Precision | decimal(18,4) shares, decimal(18,6) rates |
| Deployment | Docker Compose (3 containers) |
| API Versioning | URL path `/api/v1/` |
| Frontend State | React Query + Context |

---

## 11. CAPE API Integration (Research Affiliates)

### Decision
**Frontend-only fetch** from Research Affiliates CAPE API with **24-hour localStorage cache**.

### Rationale
- Research Affiliates provides free, public CAPE data
- No authentication required
- Frontend fetch avoids backend complexity
- 24-hour cache sufficient (CAPE updates monthly)

### API Discovery Pattern
```javascript
// Step 1: Discover latest date
const discoveryUrl = 'https://interactive.researchaffiliates.com/api/v2/cape?format=json';
// Response: { "date": "2024-12-31", ... }

// Step 2: Fetch CAPE data for that date
const dataUrl = `https://interactive.researchaffiliates.com/api/v2/cape?format=json&date=${date}`;
// Response: [{ "id": "usa", "name": "United States", "cape": 38.2, ... }, ...]
```

### Cache Strategy
```typescript
// services/capeApi.ts
const CACHE_KEY = 'cape_data';
const CACHE_DURATION = 24 * 60 * 60 * 1000; // 24 hours

export async function getCapeData(): Promise<CapeData[]> {
  const cached = localStorage.getItem(CACHE_KEY);
  if (cached) {
    const { data, timestamp } = JSON.parse(cached);
    if (Date.now() - timestamp < CACHE_DURATION) {
      return data;
    }
  }

  const freshData = await fetchCapeFromApi();
  localStorage.setItem(CACHE_KEY, JSON.stringify({
    data: freshData,
    timestamp: Date.now()
  }));
  return freshData;
}
```

### Display Format
- Show key markets: US, Taiwan, Emerging Markets, Europe
- Include CAPE value and valuation context (cheap/fair/expensive)
- Valuation thresholds: <15 cheap, 15-25 fair, >25 expensive

---

## 12. Historical Price API Integration

### Decision
**Backend service** fetching from Yahoo Finance (US/UK) and TWSE/TPEx (Taiwan), with **permanent database cache**.

### Rationale
- Historical prices are immutable (once past, they don't change)
- Database cache eliminates repeated API calls
- Backend handles API rate limits and errors gracefully
- Supports offline functionality for historical calculations

### API Sources

#### Yahoo Finance (US/UK)
```
GET https://query1.finance.yahoo.com/v8/finance/chart/{symbol}
    ?period1={unix_start}&period2={unix_end}&interval=1d

// For Dec 31 closing price:
// period1 = Dec 30 00:00 UTC
// period2 = Dec 31 23:59 UTC
// Extract: chart.result[0].indicators.adjclose[0].adjclose[-1]
```

#### TWSE (Taiwan Listed)
```
GET https://www.twse.com.tw/exchangeReport/STOCK_DAY
    ?response=json&date={YYYYMM}&stockNo={ticker}

// Returns: data array with [date, volume, open, high, low, close, ...]
// Find row matching Dec 31 (or last trading day)
```

#### TPEx (Taiwan OTC)
```
GET https://www.tpex.org.tw/web/stock/aftertrading/daily_close_quotes/stk_quote_result.php
    ?l=zh-tw&d={YYY/MM/DD}&stkno={ticker}

// YYY = year - 1911 (ROC year)
```

### Database Schema
```sql
CREATE TABLE historical_prices (
    id TEXT PRIMARY KEY,
    ticker TEXT NOT NULL,
    price_date DATE NOT NULL,
    close_price DECIMAL(18,4) NOT NULL,
    source TEXT NOT NULL,  -- 'yahoo', 'twse', 'tpex'
    fetched_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(ticker, price_date)
);

CREATE INDEX idx_historical_prices_ticker_date ON historical_prices(ticker, price_date);
```

### Cache Policy
- **Permanent cache**: Once fetched, historical prices never re-fetched
- **On-demand fetch**: Only fetch when calculating returns for specific year
- **Batch fetch**: When calculating portfolio returns, fetch all missing prices in single batch

---

## 13. Historical Returns Calculation

### Decision
**Modified Dietz method** for annual returns calculation.

### Rationale
- Simple formula that accounts for cash flows during the year
- Industry-standard for performance measurement
- Handles mid-year contributions correctly

### Formula
```
Annual Return = ((End Value - Start Value - Net Contributions) / Start Value) × 100

Where:
- End Value = Position value at Dec 31 (shares × closing price × exchange rate)
- Start Value = Position value at Jan 1 (or first purchase date if new)
- Net Contributions = Sum of Buy amounts - Sum of Sell amounts during year
```

### Implementation Details
```csharp
// Domain Service: HistoricalReturnsCalculator.cs
public record AnnualReturn(
    int Year,
    decimal StartValue,
    decimal EndValue,
    decimal NetContributions,
    decimal ReturnPercent
);

public AnnualReturn CalculateAnnualReturn(
    string ticker,
    int year,
    IEnumerable<StockTransaction> transactions,
    HistoricalPrice startPrice,
    HistoricalPrice endPrice)
{
    // 1. Get shares held at year start
    var startShares = GetSharesAtDate(transactions, new DateTime(year, 1, 1));
    var startValue = startShares * startPrice.ClosePrice * startPrice.ExchangeRate;

    // 2. Get shares held at year end
    var endShares = GetSharesAtDate(transactions, new DateTime(year, 12, 31));
    var endValue = endShares * endPrice.ClosePrice * endPrice.ExchangeRate;

    // 3. Calculate net contributions during year
    var yearTransactions = transactions
        .Where(t => t.Date.Year == year)
        .ToList();
    var netContributions = yearTransactions
        .Where(t => t.Type == TransactionType.Buy)
        .Sum(t => t.TotalCostHome)
        - yearTransactions
        .Where(t => t.Type == TransactionType.Sell)
        .Sum(t => t.TotalCostHome);

    // 4. Calculate return
    var returnPercent = startValue == 0
        ? 0
        : ((endValue - startValue - netContributions) / startValue) * 100;

    return new AnnualReturn(year, startValue, endValue, netContributions, returnPercent);
}
```

### Edge Cases
| Scenario | Handling |
|----------|----------|
| New position mid-year | Use first purchase date as start, StartValue = 0 |
| Position sold mid-year | EndValue = 0, include sell proceeds in calculation |
| No start price (new ticker) | StartValue = first purchase value |
| Missing Dec 31 price | Use last trading day of year |

---

## Summary (Updated)

| Topic | Decision |
|-------|----------|
| XIRR Algorithm | Newton-Raphson with Brent's fallback |
| Cost Calculation | Moving average, recalculate from T=0 |
| Multi-Tenancy | Row-level filtering with EF Core global filters |
| Transaction Atomicity | EF Core database transactions |
| Authentication | JWT Bearer with refresh token rotation |
| Decimal Precision | decimal(18,4) shares, decimal(18,6) rates |
| Deployment | Docker Compose (3 containers) |
| API Versioning | URL path `/api/v1/` |
| Frontend State | React Query + Context |
| **CAPE API** | Frontend fetch, 24hr localStorage cache |
| **Historical Prices** | Backend fetch, permanent DB cache |
| **Historical Returns** | Modified Dietz method |

---

## 14. Quote Caching Strategy (Revised)

### Decision
Cache is used **solely to prevent UI flickering**. Every page load triggers fresh quote fetch from API.

### Rationale
- User expects page refresh to get new data
- Cache prevents jarring "-" or empty states during initial render
- Simple mental model: refresh = new data

### Implementation Details
```typescript
// Page load sequence:
// 1. Display cached values immediately (if available)
// 2. Trigger API fetch for all positions
// 3. Update display with fresh data
// 4. Save new data to cache for next page load

const loadWithCache = async () => {
  // Step 1: Show cached immediately
  const cached = loadFromLocalStorage();
  if (cached) {
    setData(cached); // Instant display, no flicker
  }

  // Step 2-3: Fetch and update
  setIsLoading(true);
  const fresh = await fetchFromApi();
  setData(fresh);
  setIsLoading(false);

  // Step 4: Update cache
  saveToLocalStorage(fresh);
};
```

### Previous Approach (Rejected)
| Approach | Why Rejected |
|----------|--------------|
| TTL-based cache (1 hour) | User expects refresh to get new data |
| No auto-fetch on page load | Values show stale or "-" until manual click |

---

## 15. CSV Export Implementation

### Decision
**Frontend-only CSV generation** using Blob API with UTF-8 BOM for Excel compatibility.

### Rationale
- Simple implementation, no backend changes needed
- Works offline after initial data load
- BOM ensures Excel opens file with correct encoding

### Implementation Details
```typescript
// services/csvExport.ts
export function generateTransactionsCsv(transactions: StockTransaction[]): string {
  const headers = [
    '日期', '股票代號', '類型', '股數', '價格', '手續費',
    '匯率', '總成本(原幣)', '總成本(台幣)'
  ];

  const rows = transactions.map(tx => [
    formatDate(tx.transactionDate),
    tx.ticker,
    tx.transactionType === 1 ? '買入' : '賣出',
    tx.shares.toFixed(4),
    tx.pricePerShare.toFixed(4),
    tx.fees.toFixed(2),
    tx.exchangeRate.toFixed(6),
    tx.totalCostSource.toFixed(2),
    tx.totalCostHome.toFixed(0),
  ]);

  return [headers, ...rows].map(row => row.join(',')).join('\n');
}

export function downloadCsv(content: string, filename: string): void {
  // UTF-8 BOM for Excel compatibility
  const bom = '\uFEFF';
  const blob = new Blob([bom + content], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);

  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  link.click();

  URL.revokeObjectURL(url);
}
```

### Export Types
| Export | Content | Filename |
|--------|---------|----------|
| Transactions | All transaction fields | `transactions_YYYYMMDD.csv` |
| Positions | Current holdings with values | `positions_YYYYMMDD.csv` |
| Portfolio Summary | Aggregate metrics | `portfolio_summary_YYYYMMDD.csv` |

---

## 16. Market YTD Comparison

### Decision
**Backend service** fetching benchmark ETF prices with database cache for Jan 1 reference prices.

### Rationale
- Consistent with historical price approach (backend + DB cache)
- Jan 1 prices are immutable, can cache permanently
- Current prices fetched on-demand (same as position quotes)

### Benchmark ETFs
| Market | ETF | Symbol | Source |
|--------|-----|--------|--------|
| All Country | Vanguard FTSE All-World | VWRA | Sina (lse_vwra) |
| US Large | Vanguard S&P 500 | VUAA | Sina (lse_vuaa) |
| Taiwan | 元大台灣50 | 0050 | TWSE |
| Emerging Markets | Vanguard FTSE EM | VFEM | Sina (lse_vfem) |

### YTD Calculation
```csharp
// Domain Service: MarketBenchmarkService.cs
public record MarketYtdReturn(
    string MarketName,
    string Symbol,
    decimal Jan1Price,
    decimal CurrentPrice,
    decimal YtdReturnPercent
);

public async Task<MarketYtdReturn> CalculateYtd(string symbol, DateTime today)
{
    var jan1 = new DateTime(today.Year, 1, 1);
    var jan1Price = await _priceCache.GetOrFetchPrice(symbol, jan1);
    var currentPrice = await _priceService.GetCurrentPrice(symbol);

    var ytdReturn = ((currentPrice - jan1Price) / jan1Price) * 100;

    return new MarketYtdReturn(
        GetMarketName(symbol),
        symbol,
        jan1Price,
        currentPrice,
        ytdReturn
    );
}
```

### Database Schema Addition
```sql
-- Reuse historical_prices table for benchmark prices
-- Add entries with special ticker format: "BENCHMARK:{symbol}"
INSERT INTO historical_prices (id, ticker, price_date, close_price, source)
VALUES (gen_random_uuid(), 'BENCHMARK:VWRA', '2026-01-02', 120.50, 'stooq');
```

---

## 17. Taiwan Stock Currency Handling

### Decision
When source currency equals home currency (TWD), exchange rate is always **1.0**.

### Rationale
- Simplifies calculation: no currency conversion needed
- Consistent with existing exchange rate field (never null)
- UI can detect same-currency transactions for display

### Implementation Details
```csharp
// When creating Taiwan stock transaction:
var exchangeRate = portfolio.BaseCurrency == portfolio.HomeCurrency
    ? 1.0m
    : fetchedExchangeRate;

// For Taiwan stocks specifically:
if (market == StockMarket.TW)
{
    // Source currency is TWD, home currency is TWD
    exchangeRate = 1.0m;
}
```

### Frontend Display
```typescript
// Don't show "匯率" for Taiwan stocks
const isSameCurrency = baseCurrency === homeCurrency;
if (!isSameCurrency) {
  return <span>匯率: {formatNumber(exchangeRate, 4)}</span>;
}
```

---

## Summary (Updated)

| Topic | Decision |
|-------|----------|
| XIRR Algorithm | Newton-Raphson with Brent's fallback |
| Cost Calculation | Moving average, recalculate from T=0 |
| Multi-Tenancy | Row-level filtering with EF Core global filters |
| Transaction Atomicity | EF Core database transactions |
| Authentication | JWT Bearer with refresh token rotation |
| Decimal Precision | decimal(18,4) shares, decimal(18,6) rates |
| Deployment | Docker Compose (3 containers) |
| API Versioning | URL path `/api/v1/` |
| Frontend State | React Query + Context |
| CAPE API | Frontend fetch, 24hr localStorage cache |
| Historical Prices | Backend fetch, permanent DB cache |
| Historical Returns | Modified Dietz method |
| **Quote Caching** | **Flicker-prevention only, always fetch on page load** |
| **CSV Export** | **Frontend Blob API with UTF-8 BOM** |
| **Market YTD** | **Backend service with benchmark ETF prices** |
| **Taiwan Currency** | **Exchange rate = 1.0 when TWD/TWD** |

**All research complete. Ready for Phase 1: Design & Contracts.**
