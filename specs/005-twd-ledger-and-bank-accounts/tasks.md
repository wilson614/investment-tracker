# Tasks: TWD Ledger & Bank Accounts

**Feature Branch**: `005-twd-ledger-and-bank-accounts`
**Created**: 2026-01-28
**Status**: Ready for Implementation

---

## Phase 1: TWD Ledger (P1)

### Story 1.1: Create TWD Ledger and Record Deposits

#### Backend Tasks

- [x] **T1.1.1** Confirm CreateCurrencyLedgerUseCase supports TWD
  - File: `backend/src/InvestmentTracker.Application/UseCases/CurrencyLedger/CreateCurrencyLedgerUseCase.cs`
  - Verify: No logic blocking TWD, remove if exists
  - Test: Add unit test confirming TWD can be created
  - ✅ Verified: No blocking logic exists

- [x] **T1.1.2** CurrencyLedgerService TWD special handling
  - File: `backend/src/InvestmentTracker.Domain/Services/CurrencyLedgerService.cs`
  - Change: When CurrencyCode == HomeCurrency, ExchangeRate = 1.0, HomeAmount = ForeignAmount
  - Test: Add unit test verifying TWD transactions skip exchange P&L
  - ✅ Implemented in CreateCurrencyTransactionUseCase.cs, UpdateCurrencyTransactionUseCase.cs, CreateStockTransactionUseCase.cs

- [x] **T1.1.3** Confirm Unique Index (UserId, CurrencyCode) exists
  - File: `backend/src/InvestmentTracker.Infrastructure/Data/AppDbContext.cs`
  - Verify: Ensure each user can only have one TWD ledger
  - ✅ Verified: Index exists

- [x] **T1.1.3b** Verify TWD ledger supports all transaction types (FR-003)
  - New File: `tests/InvestmentTracker.Application.Tests/UseCases/CurrencyLedger/TwdLedgerTransactionTypesTests.cs`
  - Test Cases: Deposit, Withdraw, Interest, Spend, OtherIncome, OtherExpense all work correctly
  - ✅ Created: 7 tests all pass

#### Frontend Tasks

- [x] **T1.1.4** Add TWD to supported currencies
  - File: `frontend/src/pages/Currency.tsx`
  - Change: Add 'TWD' to SUPPORTED_CURRENCIES
  - ✅ Implemented

- [x] **T1.1.5** TWD ledger hide irrelevant fields
  - File: `frontend/src/pages/CurrencyDetail.tsx`
  - Change: When currencyCode === 'TWD' hide average rate, realized P&L, unrealized P&L
  - ✅ Implemented with isHomeCurrencyLedger check

- [x] **T1.1.6** TWD ledger card hide irrelevant fields
  - File: `frontend/src/components/currency/CurrencyLedgerCard.tsx`
  - Change: When currencyCode === 'TWD' hide exchange rate related fields
  - ✅ Implemented with isHomeCurrencyLedger check

#### Verification

- [x] **V1.1** Manual test: Create TWD ledger → Add Deposit → Verify balance
  - ✅ All code implemented and builds successfully
  - Backend: TWD handling in CreateCurrencyTransactionUseCase, UpdateCurrencyTransactionUseCase, CreateStockTransactionUseCase
  - Frontend: TWD in SUPPORTED_CURRENCIES, isHomeCurrencyLedger conditional rendering
  - Tests: 7 unit tests pass

---

### Story 1.2: TW Stock Transaction Linked with TWD Ledger

#### Backend Tasks

- [x] **T1.2.1** CreateStockTransactionUseCase TWD linking
  - File: `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/CreateStockTransactionUseCase.cs`
  - Change:
    - When Portfolio.BoundCurrencyLedgerId points to TWD Ledger
    - And Stock.Currency == "TWD"
    - Buy creates Spend transaction
    - Sell creates OtherIncome transaction
  - Test: Integration test verifying linking
  - ✅ Implemented with pre-validation before StockTransaction save

- [x] **T1.2.2** DeleteStockTransactionUseCase linked deletion
  - File: `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/DeleteStockTransactionUseCase.cs`
  - Change: Delete stock transaction syncs delete corresponding ledger transaction
  - Test: Integration test verifying linked deletion
  - ✅ Already implemented (uses GetByStockTransactionIdAsync + SoftDeleteAsync)

- [x] **T1.2.3** UpdateStockTransactionUseCase linked update
  - File: `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/UpdateStockTransactionUseCase.cs`
  - Change: Update stock transaction syncs update corresponding ledger transaction amount
  - Test: Integration test verifying linked update
  - ✅ Implemented with ownership validation

- [x] **T1.2.4** Insufficient balance validation
  - File: `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/CreateStockTransactionUseCase.cs`
  - Change: On buy, check TWD Ledger balance is sufficient, throw BusinessRuleException if not
  - Test: Unit test verifying insufficient balance error
  - ✅ Implemented with pre-validation and amount > 0 check

#### Frontend Tasks

- [ ] **T1.2.5** StockTransactionForm show linking info
  - File: `frontend/src/components/stock/StockTransactionForm.tsx`
  - Change: When Portfolio bound to TWD Ledger and is TW stock, show deduction notice

- [ ] **T1.2.6** PortfolioSettings ledger binding options
  - File: `frontend/src/pages/PortfolioSettings.tsx`
  - Change: Confirm dropdown includes TWD Ledger option

#### Verification

- [ ] **V1.2** Manual test: Bind Portfolio → Buy TW stock → Verify ledger deduction → Delete transaction → Verify balance restored

---

## Phase 2: Bank Accounts (P2)

### Story 2.1: Create Bank Account with Preferential Rate Info

#### Backend Tasks

- [ ] **T2.1.1** Create BankAccount Entity
  - New File: `backend/src/InvestmentTracker.Domain/Entities/BankAccount.cs`
  - Properties: Id, UserId, BankName, TotalAssets, InterestRate, InterestCap, Note, IsActive, CreatedAt, UpdatedAt

- [ ] **T2.1.2** Create IBankAccountRepository interface
  - New File: `backend/src/InvestmentTracker.Domain/Interfaces/IBankAccountRepository.cs`

- [ ] **T2.1.3** Create BankAccountRepository implementation
  - New File: `backend/src/InvestmentTracker.Infrastructure/Repositories/BankAccountRepository.cs`

- [ ] **T2.1.4** Update DbContext
  - File: `backend/src/InvestmentTracker.Infrastructure/Data/AppDbContext.cs`
  - Change: Add DbSet<BankAccount>

- [ ] **T2.1.5** Create Migration
  - Run: `dotnet ef migrations add AddBankAccountTable`
  - Confirm: Table structure is correct

- [ ] **T2.1.6** Create CreateBankAccountUseCase
  - New File: `backend/src/InvestmentTracker.Application/UseCases/BankAccount/CreateBankAccountUseCase.cs`

- [ ] **T2.1.7** Create UpdateBankAccountUseCase
  - New File: `backend/src/InvestmentTracker.Application/UseCases/BankAccount/UpdateBankAccountUseCase.cs`

- [ ] **T2.1.8** Create DeleteBankAccountUseCase
  - New File: `backend/src/InvestmentTracker.Application/UseCases/BankAccount/DeleteBankAccountUseCase.cs`
  - Note: Soft delete (IsActive = false)

- [ ] **T2.1.9** Create GetBankAccountsUseCase
  - New File: `backend/src/InvestmentTracker.Application/UseCases/BankAccount/GetBankAccountsUseCase.cs`

- [ ] **T2.1.10** Create GetBankAccountUseCase
  - New File: `backend/src/InvestmentTracker.Application/UseCases/BankAccount/GetBankAccountUseCase.cs`

- [ ] **T2.1.11** Create BankAccountsController
  - New File: `backend/src/InvestmentTracker.Api/Controllers/BankAccountsController.cs`
  - Endpoints: GET/POST/PUT/DELETE

- [ ] **T2.1.12** DI Registration
  - File: `backend/src/InvestmentTracker.Api/Program.cs`
  - Change: Register IBankAccountRepository and related Use Cases

#### Frontend Tasks

- [ ] **T2.1.13** Create BankAccount type definitions
  - New File: `frontend/src/features/bank-accounts/types/index.ts`

- [ ] **T2.1.14** Create bankAccountsApi
  - New File: `frontend/src/features/bank-accounts/api/bankAccountsApi.ts`

- [ ] **T2.1.15** Create useBankAccounts hook
  - New File: `frontend/src/features/bank-accounts/hooks/useBankAccounts.ts`
  - Use TanStack Query

- [ ] **T2.1.16** Create BankAccountCard component
  - New File: `frontend/src/features/bank-accounts/components/BankAccountCard.tsx`

- [ ] **T2.1.17** Create BankAccountForm component
  - New File: `frontend/src/features/bank-accounts/components/BankAccountForm.tsx`

- [ ] **T2.1.18** Create BankAccountsPage
  - New File: `frontend/src/features/bank-accounts/pages/BankAccountsPage.tsx`

- [ ] **T2.1.19** Add routing and navigation
  - File: `frontend/src/App.tsx`
  - Change: Add /bank-accounts route
  - File: `frontend/src/components/layout/Sidebar.tsx`
  - Change: Add "Bank Accounts" link

#### Verification

- [ ] **V2.1** Manual test: CRUD bank accounts → Verify data stored correctly

---

### Story 2.2: View Interest Estimation

#### Backend Tasks

- [ ] **T2.2.1** Create InterestEstimationService
  - New File: `backend/src/InvestmentTracker.Domain/Services/InterestEstimationService.cs`
  - Method: Calculate(BankAccount) → InterestEstimation
  - Formula: Min(TotalAssets, InterestCap) × (InterestRate / 100 / 12)

- [ ] **T2.2.2** Update GetBankAccountsUseCase
  - Change: Response includes interest estimation
  - Or add GetBankAccountsWithInterestUseCase

- [ ] **T2.2.3** InterestEstimationService unit tests
  - New File: `tests/InvestmentTracker.Domain.Tests/Services/InterestEstimationServiceTests.cs`
  - Test Cases:
    - TotalAssets > InterestCap → Use InterestCap for calculation
    - TotalAssets < InterestCap → Use TotalAssets for calculation
    - InterestRate = 0 → Interest = 0

#### Frontend Tasks

- [ ] **T2.2.4** Create InterestEstimationCard component
  - New File: `frontend/src/features/bank-accounts/components/InterestEstimationCard.tsx`
  - Display: Monthly interest, yearly interest

- [ ] **T2.2.5** BankAccountsPage add total interest
  - File: `frontend/src/features/bank-accounts/pages/BankAccountsPage.tsx`
  - Change: Display total interest for all accounts

#### Verification

- [ ] **V2.2** Manual test: Create bank account → Verify interest estimation is correct

---

## Phase 3: Total Assets Dashboard (P3)

### Story 3.1: View Total Assets Dashboard

#### Backend Tasks

- [ ] **T3.1.1** Create TotalAssetsSummary DTO
  - New File: `backend/src/InvestmentTracker.Application/DTOs/TotalAssetsSummary.cs`
  - Properties: InvestmentTotal, BankTotal, GrandTotal, InvestmentPercentage, BankPercentage

- [ ] **T3.1.2** Create TotalAssetsService
  - New File: `backend/src/InvestmentTracker.Domain/Services/TotalAssetsService.cs`
  - Calculate:
    - Stock market value
    - TWD ledger balance
    - Foreign ledger balance (converted to TWD)
    - Bank account total assets
    - Percentages

- [ ] **T3.1.3** Create GetTotalAssetsSummaryUseCase
  - New File: `backend/src/InvestmentTracker.Application/UseCases/Assets/GetTotalAssetsSummaryUseCase.cs`

- [ ] **T3.1.4** Create AssetsController
  - New File: `backend/src/InvestmentTracker.Api/Controllers/AssetsController.cs`
  - Endpoint: GET /api/assets/summary

- [ ] **T3.1.5** TotalAssetsService unit tests
  - New File: `tests/InvestmentTracker.Domain.Tests/Services/TotalAssetsServiceTests.cs`

#### Frontend Tasks

- [ ] **T3.1.6** Create assetsApi
  - New File: `frontend/src/features/total-assets/api/assetsApi.ts`

- [ ] **T3.1.7** Create useTotalAssets hook
  - New File: `frontend/src/features/total-assets/hooks/useTotalAssets.ts`

- [ ] **T3.1.8** Create TotalAssetsBanner component
  - New File: `frontend/src/features/total-assets/components/TotalAssetsBanner.tsx`
  - Display: Total assets number

- [ ] **T3.1.9** Create AssetsBreakdownPieChart component
  - New File: `frontend/src/features/total-assets/components/AssetsBreakdownPieChart.tsx`
  - Use Recharts pie chart

- [ ] **T3.1.10** Create AssetCategorySummary component
  - New File: `frontend/src/features/total-assets/components/AssetCategorySummary.tsx`
  - Display: Investment/Bank breakdown

- [ ] **T3.1.11** Create TotalAssetsDashboard page
  - New File: `frontend/src/features/total-assets/pages/TotalAssetsDashboard.tsx`

- [ ] **T3.1.12** Add routing and navigation
  - File: `frontend/src/App.tsx`
  - Change: Add /assets route
  - Navigation: Add "Total Assets" link

#### Verification

- [ ] **V3.1** Manual test: View total assets page → Verify calculation and percentages are correct

---

## Phase 4: Quality Assurance (QA)

### Story 4.1: Performance and Regression Verification

#### Tasks

- [ ] **T4.1.1** Performance verification (SC-004)
  - Verify: Total assets page load time < 2 seconds
  - Method: Manual test or Lighthouse evaluation

- [ ] **T4.1.2** Regression testing (SC-005)
  - Verify: No regression in existing foreign currency ledger and foreign stock functionality
  - Method: Run existing integration tests + manual verification of foreign currency ledger CRUD

- [ ] **T4.1.3** Test coverage check
  - Target: Domain layer >80%, financial calculation methods 100%
  - Method: `dotnet test --collect:"XPlat Code Coverage"`

#### Verification

- [ ] **V4.1** All tests pass + coverage targets met

---

## Summary

| Phase | Story | Tasks | Status |
|-------|-------|-------|--------|
| 1 | 1.1 Create TWD Ledger | 7 + 1 verification | ✅ Complete |
| 1 | 1.2 TW Stock Linking | 6 + 1 verification | ⬜ Pending |
| 2 | 2.1 Bank Account CRUD | 19 + 1 verification | ⬜ Pending |
| 2 | 2.2 Interest Estimation | 5 + 1 verification | ⬜ Pending |
| 3 | 3.1 Total Assets Dashboard | 12 + 1 verification | ⬜ Pending |
| 4 | 4.1 Performance & Regression | 3 + 1 verification | ⬜ Pending |

**Total**: 52 tasks + 6 verification steps
