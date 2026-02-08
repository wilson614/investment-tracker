# Quickstart: Fixed Deposit and Credit Card Installment Tracking

**Feature**: 008-fixed-deposit-installment
**Date**: 2026-02-08

## Prerequisites

- .NET 8 SDK installed
- Node.js 18+ installed
- PostgreSQL running (via Docker or local)
- Project cloned and dependencies restored

## Backend Setup

### 1. Apply Database Migration

```bash
cd backend/src/InvestmentTracker.API
dotnet ef migrations add AddFixedDepositAndInstallment -p ../InvestmentTracker.Infrastructure
dotnet ef database update
```

### 2. Register New Services

Add to `Program.cs` or DI configuration:

```csharp
// Repositories
builder.Services.AddScoped<IFixedDepositRepository, FixedDepositRepository>();
builder.Services.AddScoped<ICreditCardRepository, CreditCardRepository>();
builder.Services.AddScoped<IInstallmentRepository, InstallmentRepository>();

// Domain Services
builder.Services.AddScoped<AvailableFundsService>();

// Use Cases - Fixed Deposits
builder.Services.AddScoped<GetFixedDepositsUseCase>();
builder.Services.AddScoped<CreateFixedDepositUseCase>();
builder.Services.AddScoped<UpdateFixedDepositUseCase>();
builder.Services.AddScoped<CloseFixedDepositUseCase>();

// Use Cases - Credit Cards
builder.Services.AddScoped<GetCreditCardsUseCase>();
builder.Services.AddScoped<CreateCreditCardUseCase>();
builder.Services.AddScoped<UpdateCreditCardUseCase>();
builder.Services.AddScoped<DeactivateCreditCardUseCase>();

// Use Cases - Installments
builder.Services.AddScoped<GetInstallmentsUseCase>();
builder.Services.AddScoped<CreateInstallmentUseCase>();
builder.Services.AddScoped<UpdateInstallmentUseCase>();
builder.Services.AddScoped<RecordPaymentUseCase>();
builder.Services.AddScoped<PayoffInstallmentUseCase>();
builder.Services.AddScoped<GetUpcomingPaymentsUseCase>();

// Use Cases - Available Funds
builder.Services.AddScoped<GetAvailableFundsSummaryUseCase>();
```

### 3. Add DbSet to AppDbContext

```csharp
public DbSet<FixedDeposit> FixedDeposits => Set<FixedDeposit>();
public DbSet<CreditCard> CreditCards => Set<CreditCard>();
public DbSet<Installment> Installments => Set<Installment>();
```

### 4. Run Backend

```bash
cd backend/src/InvestmentTracker.API
dotnet run
```

API available at `http://localhost:5000`

## Frontend Setup

### 1. Create Feature Folders

```bash
cd frontend/src/features
mkdir -p fixed-deposits/{api,components,hooks,types}
mkdir -p credit-cards/{api,components,hooks,types}
```

### 2. Add Routes

In router configuration:

```tsx
// Add routes for new features
{ path: '/fixed-deposits', element: <FixedDepositsPage /> },
{ path: '/credit-cards', element: <CreditCardsPage /> },
{ path: '/credit-cards/:cardId/installments', element: <InstallmentsPage /> },
```

### 3. Add Navigation

Add menu items for new sections:

```tsx
<NavItem to="/fixed-deposits">定存</NavItem>
<NavItem to="/credit-cards">信用卡</NavItem>
```

### 4. Run Frontend

```bash
cd frontend
npm run dev
```

App available at `http://localhost:3000`

## API Testing

### Create Fixed Deposit

```bash
curl -X POST http://localhost:5000/api/fixed-deposits \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "bankName": "台灣銀行",
    "principal": 100000,
    "annualInterestRate": 0.015,
    "termMonths": 12,
    "startDate": "2026-02-01",
    "currency": "TWD"
  }'
```

### Create Credit Card

```bash
curl -X POST http://localhost:5000/api/credit-cards \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "bankName": "中國信託",
    "cardName": "Costco聯名卡",
    "billingCycleDay": 15
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
    "startDate": "2026-02-01"
  }'
```

### Get Available Funds Summary

```bash
curl http://localhost:5000/api/available-funds \
  -H "Authorization: Bearer $TOKEN"
```

## Key Files Reference

| Layer | Path | Description |
|-------|------|-------------|
| Domain Entity | `Domain/Entities/FixedDeposit.cs` | Fixed deposit entity |
| Domain Entity | `Domain/Entities/CreditCard.cs` | Credit card entity |
| Domain Entity | `Domain/Entities/Installment.cs` | Installment entity |
| Domain Service | `Domain/Services/AvailableFundsService.cs` | Available funds calculation |
| Repository | `Infrastructure/Repositories/FixedDepositRepository.cs` | Fixed deposit data access |
| Use Case | `Application/UseCases/FixedDeposits/` | Fixed deposit business logic |
| Controller | `API/Controllers/FixedDepositsController.cs` | Fixed deposit API endpoints |
| Frontend API | `features/fixed-deposits/api/` | API client functions |
| Frontend Hook | `features/fixed-deposits/hooks/` | React Query hooks |
| Frontend Component | `features/fixed-deposits/components/` | UI components |

## Testing Checklist

- [ ] Create fixed deposit with various currencies
- [ ] Verify maturity date calculation
- [ ] Close fixed deposit (normal maturity)
- [ ] Close fixed deposit (early withdrawal)
- [ ] Create credit card
- [ ] Create installment on card
- [ ] Record monthly payment
- [ ] Early payoff installment
- [ ] View upcoming payments
- [ ] Verify available funds calculation
- [ ] Check dashboard displays correct breakdown
