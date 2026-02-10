# Quickstart: Fixed Deposit and Credit Card Installment Tracking

**Feature**: 008-fixed-deposit-installment
**Date**: 2026-02-08

## Prerequisites

- .NET 8 SDK installed
- Node.js 18+ installed
- PostgreSQL running (via Docker or local)
- Project cloned and dependencies restored

## Backend Setup

### 1. Apply Database Migrations

```bash
cd backend/src/InvestmentTracker.API
dotnet ef database update
```

This feature's evolution includes these relevant migrations:
- `AddFixedDepositAndInstallment`
- `MergeFixedDepositIntoBankAccount`
- `RemoveCreditCardIsActive`
- `RenameCreditCardBillingCycleDayToPaymentDueDay`
- `RenameStartDateToFirstPaymentDate`

### 2. DI Registration Reference

In `Program.cs`, ensure these services are registered:

```csharp
// Repositories
builder.Services.AddScoped<IBankAccountRepository, BankAccountRepository>();
builder.Services.AddScoped<ICreditCardRepository, CreditCardRepository>();
builder.Services.AddScoped<IInstallmentRepository, InstallmentRepository>();

// Domain Services
builder.Services.AddScoped<InterestEstimationService>();
builder.Services.AddScoped<TotalAssetsService>();

// Use Cases - Bank Accounts (fixed deposits included)
builder.Services.AddScoped<GetBankAccountsUseCase>();
builder.Services.AddScoped<GetBankAccountUseCase>();
builder.Services.AddScoped<CreateBankAccountUseCase>();
builder.Services.AddScoped<UpdateBankAccountUseCase>();
builder.Services.AddScoped<DeleteBankAccountUseCase>();
builder.Services.AddScoped<CloseBankAccountUseCase>();

// Use Cases - Credit Cards
builder.Services.AddScoped<GetCreditCardsUseCase>();
builder.Services.AddScoped<GetCreditCardUseCase>();
builder.Services.AddScoped<CreateCreditCardUseCase>();
builder.Services.AddScoped<UpdateCreditCardUseCase>();

// Use Cases - Installments
builder.Services.AddScoped<GetInstallmentsUseCase>();
builder.Services.AddScoped<GetAllUserInstallmentsUseCase>();
builder.Services.AddScoped<CreateInstallmentUseCase>();
builder.Services.AddScoped<DeleteInstallmentUseCase>();
builder.Services.AddScoped<GetUpcomingPaymentsUseCase>();

// Use Cases - Assets Summary (installments integrated)
builder.Services.AddScoped<GetTotalAssetsSummaryUseCase>();
```

### 3. AppDbContext Reference

```csharp
public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
public DbSet<CreditCard> CreditCards => Set<CreditCard>();
public DbSet<Installment> Installments => Set<Installment>();
```

> Note: There is no standalone `DbSet<FixedDeposit>` in the current implementation.

### 4. Run Backend

```bash
cd backend/src/InvestmentTracker.API
dotnet run
```

API available at `http://localhost:5000`

## Frontend Setup

### 1. Feature Structure Reference

Current implementation uses:

```text
frontend/src/features/
├── bank-accounts/   # savings + fixed deposit UI
├── credit-cards/    # card + installment UI
└── total-assets/    # summary includes installment unpaid balance
```

> There is no `frontend/src/features/fixed-deposits/` folder in current implementation.

### 2. Run Frontend

```bash
cd frontend
npm run dev
```

App available at `http://localhost:3000`

## API Testing

### Create Fixed Deposit (via BankAccount API)

```bash
curl -X POST http://localhost:5000/api/bank-accounts \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "bankName": "Bank of Taiwan",
    "totalAssets": 100000,
    "interestRate": 1.5,
    "interestCap": 0,
    "currency": "TWD",
    "accountType": "FixedDeposit",
    "termMonths": 12,
    "startDate": "2026-02-01T00:00:00Z"
  }'
```

### Create Credit Card

```bash
curl -X POST http://localhost:5000/api/credit-cards \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "bankName": "CTBC Bank",
    "cardName": "Costco Co-Branded Card",
    "paymentDueDay": 15
  }'
```

### Create Installment

```bash
curl -X POST http://localhost:5000/api/credit-cards/{cardId}/installments \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "description": "iPhone 15 Pro",
    "totalAmount": 45900,
    "numberOfInstallments": 12,
    "firstPaymentDate": "2026-03-01T00:00:00Z"
  }'
```

### Get Total Assets Summary (includes installment unpaid balance)

```bash
curl http://localhost:5000/api/assets/summary \
  -H "Authorization: Bearer $TOKEN"
```

## Key Files Reference

| Layer | Path | Description |
|-------|------|-------------|
| Domain Entity | `backend/src/InvestmentTracker.Domain/Entities/BankAccount.cs` | Unified account model (savings + fixed deposit) |
| Domain Entity | `backend/src/InvestmentTracker.Domain/Entities/CreditCard.cs` | Credit card aggregate |
| Domain Entity | `backend/src/InvestmentTracker.Domain/Entities/Installment.cs` | Installment aggregate |
| Domain Service | `backend/src/InvestmentTracker.Domain/Services/TotalAssetsService.cs` | Assets summary calculation |
| Use Case | `backend/src/InvestmentTracker.Application/UseCases/BankAccount/` | Bank-account use cases (includes fixed deposit lifecycle) |
| Use Case | `backend/src/InvestmentTracker.Application/UseCases/CreditCards/` | Credit card business logic |
| Use Case | `backend/src/InvestmentTracker.Application/UseCases/Installments/` | Installment business logic |
| Controller | `backend/src/InvestmentTracker.API/Controllers/BankAccountsController.cs` | Bank account API (fixed deposits included) |
| Controller | `backend/src/InvestmentTracker.API/Controllers/CreditCardsController.cs` | Credit card API |
| Controller | `backend/src/InvestmentTracker.API/Controllers/InstallmentsController.cs` | Installment API |
| Controller | `backend/src/InvestmentTracker.API/Controllers/AssetsController.cs` | Total assets summary API |
| Frontend API | `frontend/src/features/bank-accounts/api/bankAccountsApi.ts` | Bank account client (fixed deposits included) |
| Frontend API | `frontend/src/features/credit-cards/api/` | Credit card + installment clients |
| Frontend Summary | `frontend/src/features/total-assets/components/NonDisposableAssetsSection.tsx` | Displays installment unpaid balance |

## Testing Checklist

- [ ] Create fixed deposit account via `/api/bank-accounts` with `accountType = FixedDeposit`
- [ ] Verify fixed deposit maturity date and expected interest are returned
- [ ] Verify fixed-deposit UI appears under bank-accounts feature
- [ ] Verify fixed-deposit form hides interest cap field
- [ ] Create credit card with `paymentDueDay`
- [ ] Create installment with `firstPaymentDate`
- [ ] Verify installment day is auto-adjusted to card payment due day
- [ ] Verify installment remaining/unpaid values are auto-calculated over time
- [ ] Delete installment and confirm list/summary updates
- [ ] Verify total-assets summary includes installment unpaid balance in non-disposable assets
- [ ] Verify TWD total assets input enforces integer entry while foreign currencies allow decimals
