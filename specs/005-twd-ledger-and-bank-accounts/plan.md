# Implementation Plan: TWD Ledger & Bank Accounts

**Feature Branch**: `005-twd-ledger-and-bank-accounts`
**Created**: 2026-01-28
**Status**: ✅ Complete

---

## Design Change Summary

### Previous Design (Complex)
- Portfolio optionally binds Ledger
- Complex currency inference logic to determine if linking should happen
- TWD-specific handling in CreateStockTransactionUseCase

### New Design (Simplified 1:1 Model)
- **Portfolio : CurrencyLedger = 1:1 mandatory binding**
- **Stock.Currency must match Ledger.CurrencyCode** (validation)
- **All Buy/Sell auto-link** (no currency inference needed)
- **Binding is permanent** (no unbind)

---

## Phase 0: Data Model Refactoring (NEW)

### 0.1 Backend - Enforce 1:1 Binding

#### 0.1.1 Portfolio Entity Changes
- **File**: `backend/src/InvestmentTracker.Domain/Entities/Portfolio.cs`
- **Changes**:
  - Make `BoundCurrencyLedgerId` required (not nullable)
  - Remove `BindCurrencyLedger()` method (binding happens at creation)
  - Add constructor that requires CurrencyLedgerId

#### 0.1.2 CurrencyLedger Unique Constraint
- **File**: `backend/src/InvestmentTracker.Infrastructure/Persistence/Configurations/PortfolioConfiguration.cs`
- **Change**: Add unique index on `BoundCurrencyLedgerId` (one ledger = one portfolio)

#### 0.1.3 CreatePortfolioUseCase Changes
- **File**: `backend/src/InvestmentTracker.Application/UseCases/Portfolio/CreatePortfolioUseCase.cs`
- **Changes**:
  - Require `CurrencyLedgerId` in request
  - Validate ledger exists and belongs to user
  - Validate ledger is not already bound to another portfolio

#### 0.1.4 Remove FundSource Logic
- **Files**: Multiple Use Cases
- **Change**: Remove `FundSource` enum usage, all transactions auto-link to bound ledger

### 0.2 Backend - Simplify Stock Transaction Linking

#### 0.2.1 CreateStockTransactionUseCase Refactor
- **File**: `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/CreateStockTransactionUseCase.cs`
- **Changes**:
  - Remove complex currency inference logic (lines 86-88)
  - Get bound ledger from Portfolio.BoundCurrencyLedgerId (always exists)
  - Validate `Stock.Currency == Ledger.CurrencyCode`
  - Always create linked transaction (Buy=Spend, Sell=OtherIncome)
  - Remove FundSource parameter handling

#### 0.2.2 UpdateStockTransactionUseCase Refactor
- **File**: `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/UpdateStockTransactionUseCase.cs`
- **Change**: Same simplification as Create

#### 0.2.3 DeleteStockTransactionUseCase
- **File**: `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/DeleteStockTransactionUseCase.cs`
- **Verify**: Linked deletion already works via RelatedStockTransactionId

### 0.3 Database Migration

#### 0.3.1 Migration Script
- Make `BoundCurrencyLedgerId` NOT NULL (requires data wipe or migration)
- Add unique index on `BoundCurrencyLedgerId`
- Remove FundSource column from StockTransactions (if exists)

### 0.4 Frontend - Simplify UI

#### 0.4.1 Remove FundSource Selection
- **File**: `frontend/src/components/transactions/TransactionForm.tsx`
- **Change**: Remove FundSource dropdown, always show "Will deduct from [Ledger Name]"

#### 0.4.2 Portfolio Creation Flow
- **File**: `frontend/src/pages/Portfolio.tsx` (or CreatePortfolioModal)
- **Change**: Must select/create a CurrencyLedger when creating portfolio

#### 0.4.3 Remove Currency-Specific Logic
- **Files**: Various components
- **Change**: Remove TWD-specific conditionals, use generic "home currency" check

---

## Phase 1: Home Currency Ledger Support (Simplified)

### 1.1 Backend - TWD Ledger Support

#### 1.1.1 Remove Currency Restriction
- **File**: `backend/src/InvestmentTracker.Application/UseCases/CurrencyLedger/CreateCurrencyLedgerUseCase.cs`
- **Change**: Confirm no logic blocking TWD ledger creation (currently none)
- **Verify**: Unit test confirms CurrencyCode="TWD" can be created

#### 1.1.2 CurrencyLedgerService TWD Special Handling
- **File**: `backend/src/InvestmentTracker.Domain/Services/CurrencyLedgerService.cs`
- **Change**:
  ```csharp
  // When CurrencyCode == HomeCurrency
  if (ledger.CurrencyCode == homeCurrency)
  {
      transaction.ExchangeRate = 1.0m;
      transaction.HomeAmount = transaction.ForeignAmount;
      // Skip exchange P&L calculation
  }
  ```
- **Scope**: `AddTransaction`, `RecalculateLedgerTotals` methods

#### 1.1.3 Home Currency Special Handling
- **File**: `backend/src/InvestmentTracker.Application/UseCases/CurrencyLedger/CreateCurrencyTransactionUseCase.cs`
- **Change**: When Ledger.CurrencyCode == HomeCurrency, set ExchangeRate=1.0, HomeAmount=ForeignAmount
- **Already Implemented**: Verify existing logic works for any home currency

### 1.2 Frontend - TWD Ledger UI

#### 1.2.1 Support TWD Currency
- **File**: `frontend/src/pages/Currency.tsx`
- **Change**: Add `'TWD'` to `SUPPORTED_CURRENCIES` array

#### 1.2.2 Conditional Field Hiding
- **Files**:
  - `frontend/src/pages/CurrencyDetail.tsx`
  - `frontend/src/components/currency/CurrencyLedgerCard.tsx`
- **Change**: When `currencyCode === 'TWD'` hide:
  - Average exchange rate
  - Unrealized P&L
  - Realized P&L

#### 1.2.3 Portfolio-Ledger Binding UI
- **File**: `frontend/src/pages/Portfolio.tsx`
- **Change**:
  - Creating portfolio requires selecting a CurrencyLedger
  - Display bound ledger info in portfolio header
  - Remove settings modal (binding is permanent)

---

## Phase 2: Bank Accounts (P2)

### 2.1 Backend - BankAccount Entity

#### 2.1.1 Domain Entity
- **New File**: `backend/src/InvestmentTracker.Domain/Entities/BankAccount.cs`
- **Properties**:
  ```csharp
  public class BankAccount : BaseEntity
  {
      public Guid UserId { get; set; }
      public string BankName { get; set; } = string.Empty;
      public decimal TotalAssets { get; set; }
      public decimal InterestRate { get; set; }  // Annual rate %
      public decimal InterestCap { get; set; }   // Preferential cap limit
      public string? Note { get; set; }
      public bool IsActive { get; set; } = true;
  }
  ```

#### 2.1.2 Repository
- **New File**: `backend/src/InvestmentTracker.Infrastructure/Repositories/BankAccountRepository.cs`
- **New Interface**: `backend/src/InvestmentTracker.Domain/Interfaces/IBankAccountRepository.cs`

#### 2.1.3 Use Cases
- **New Files**:
  - `CreateBankAccountUseCase.cs`
  - `UpdateBankAccountUseCase.cs`
  - `DeleteBankAccountUseCase.cs` (soft delete IsActive = false)
  - `GetBankAccountsUseCase.cs`
  - `GetBankAccountUseCase.cs`

#### 2.1.4 Controller
- **New File**: `backend/src/InvestmentTracker.Api/Controllers/BankAccountsController.cs`
- **Endpoints**:
  - `GET /api/bank-accounts`
  - `GET /api/bank-accounts/{id}`
  - `POST /api/bank-accounts`
  - `PUT /api/bank-accounts/{id}`
  - `DELETE /api/bank-accounts/{id}`

#### 2.1.5 Interest Estimation Service
- **New File**: `backend/src/InvestmentTracker.Domain/Services/InterestEstimationService.cs`
- **Method**:
  ```csharp
  public InterestEstimation Calculate(BankAccount account)
  {
      var effectivePrincipal = Math.Min(account.TotalAssets, account.InterestCap);
      var monthlyInterest = effectivePrincipal * (account.InterestRate / 100m / 12m);
      var yearlyInterest = effectivePrincipal * (account.InterestRate / 100m);
      return new InterestEstimation(monthlyInterest, yearlyInterest);
  }
  ```

### 2.2 Frontend - BankAccount UI

#### 2.2.1 Directory Structure
```
frontend/src/features/bank-accounts/
├── api/
│   └── bankAccountsApi.ts
├── components/
│   ├── BankAccountCard.tsx
│   ├── BankAccountForm.tsx
│   └── InterestEstimationCard.tsx
├── hooks/
│   └── useBankAccounts.ts
├── pages/
│   └── BankAccountsPage.tsx
└── types/
    └── index.ts
```

#### 2.2.2 Routing & Navigation
- **File**: `frontend/src/App.tsx`
- **New Route**: `/bank-accounts`
- **Sidebar**: Add "Bank Accounts" link

#### 2.2.3 Page Features
- Bank account list (card style)
- Add/Edit form Modal
- Per-account interest estimation display
- Total interest estimation display

---

## Phase 3: Total Assets Dashboard (P3)

### 3.1 Backend - Assets Summary API

#### 3.1.1 Total Assets Service
- **New File**: `backend/src/InvestmentTracker.Domain/Services/TotalAssetsService.cs`
- **Calculation Logic**:
  ```csharp
  public TotalAssetsSummary Calculate(Guid userId)
  {
      // Investment
      var stockMarketValue = GetTotalStockMarketValue(userId);
      var twdLedgerBalance = GetTwdLedgerBalance(userId);
      var foreignLedgerBalanceInTwd = GetForeignLedgersBalanceInTwd(userId);
      var investmentTotal = stockMarketValue + twdLedgerBalance + foreignLedgerBalanceInTwd;

      // Bank Assets
      var bankTotal = GetTotalBankAssets(userId);

      // Total Assets
      var grandTotal = investmentTotal + bankTotal;

      return new TotalAssetsSummary
      {
          InvestmentTotal = investmentTotal,
          BankTotal = bankTotal,
          GrandTotal = grandTotal,
          InvestmentPercentage = grandTotal > 0 ? investmentTotal / grandTotal * 100 : 0,
          BankPercentage = grandTotal > 0 ? bankTotal / grandTotal * 100 : 0
      };
  }
  ```

#### 3.1.2 Controller
- **File**: `backend/src/InvestmentTracker.Api/Controllers/AssetsController.cs`
- **Endpoint**: `GET /api/assets/summary`

### 3.2 Frontend - Dashboard UI

#### 3.2.1 Directory Structure
```
frontend/src/features/total-assets/
├── api/
│   └── assetsApi.ts
├── components/
│   ├── AssetsBreakdownPieChart.tsx
│   ├── AssetCategorySummary.tsx
│   └── TotalAssetsBanner.tsx
├── hooks/
│   └── useTotalAssets.ts
└── pages/
    └── TotalAssetsDashboard.tsx
```

#### 3.2.2 Routing & Navigation
- **New Route**: `/assets` or `/dashboard/total`
- **Navigation**: Add "Total Assets" link (consider top-level placement)

#### 3.2.3 Page Features
- Total assets number display
- Investment vs Bank ratio pie chart (Recharts)
- Investment breakdown (stocks, TWD ledger, foreign ledgers)
- Bank assets breakdown (per bank)

---

## Database Migration

### Migration Order
1. `AddBankAccountTable` - Add BankAccount table
   ```sql
   CREATE TABLE "BankAccounts" (
       "Id" uuid PRIMARY KEY,
       "UserId" uuid NOT NULL,
       "BankName" text NOT NULL,
       "TotalAssets" numeric NOT NULL,
       "InterestRate" numeric NOT NULL,
       "InterestCap" numeric NOT NULL,
       "Note" text,
       "IsActive" boolean NOT NULL DEFAULT true,
       "CreatedAt" timestamp NOT NULL,
       "UpdatedAt" timestamp NOT NULL
   );
   CREATE INDEX "IX_BankAccounts_UserId" ON "BankAccounts" ("UserId");
   ```

---

## Testing Strategy

### Coverage Targets (Constitution Requirements)
- **Domain Layer**: >80% unit test coverage
- **Financial Calculation Methods**: 100% coverage (InterestEstimationService, TotalAssetsService)
- **Integration Tests**: All critical user journeys

### Unit Tests
- `CurrencyLedgerServiceTests` - TWD special handling
- `InterestEstimationServiceTests` - Interest calculation formula
- `TotalAssetsServiceTests` - Total assets calculation

### Integration Tests
- TW stock transaction linked to TWD Ledger
- Bank account CRUD
- Total assets API returns correct data

### E2E Tests (Optional)
- Create TWD ledger → Deposit funds → Buy TW stock → Verify ledger balance
- Create bank account → Set rate and cap → Verify interest estimation

---

## Risks & Mitigation

| Risk | Impact | Mitigation |
|------|--------|------------|
| Existing foreign currency ledger regression | High | Complete unit/integration test coverage for existing functionality |
| TW stock amount decimal issues | Medium | Use decimal uniformly, round on frontend display |
| Performance issues (total assets calculation) | Low | Consider caching, but skip for MVP |

---

## Implementation Order

1. **Phase 1.1** - Backend TWD support (no UI changes)
2. **Phase 1.2** - Frontend TWD support
3. **Phase 2.1** - Backend BankAccount (complete CRUD)
4. **Phase 2.2** - Frontend BankAccount
5. **Phase 3.1** - Backend total assets API
6. **Phase 3.2** - Frontend total assets dashboard

Each phase can be independently deployed and tested.
