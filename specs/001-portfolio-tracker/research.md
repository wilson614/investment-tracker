# Research: Family Investment Portfolio Tracker

**Feature**: 001-portfolio-tracker
**Date**: 2026-01-06
**Status**: Complete

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

**All research complete. Ready for Phase 1: Design & Contracts.**
