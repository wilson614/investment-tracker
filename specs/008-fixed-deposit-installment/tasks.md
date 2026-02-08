# Tasks: Fixed Deposit and Credit Card Installment Tracking

**Input**: Design documents from `/specs/008-fixed-deposit-installment/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Constitution mandates 100% test coverage for financial calculations. Test tasks included for AvailableFundsService (core liquidity calculation).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Include exact file paths in descriptions

## Path Conventions

- **Backend**: `backend/src/InvestmentTracker.{Layer}/`
- **Frontend**: `frontend/src/features/{feature-name}/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Database schema and shared enums/interfaces

- [ ] T001 [P] Create FixedDepositStatus enum in `backend/src/InvestmentTracker.Domain/Enums/FixedDepositStatus.cs`
- [ ] T002 [P] Create InstallmentStatus enum in `backend/src/InvestmentTracker.Domain/Enums/InstallmentStatus.cs`
- [ ] T003 [P] Create IFixedDepositRepository interface in `backend/src/InvestmentTracker.Domain/Interfaces/IFixedDepositRepository.cs`
- [ ] T004 [P] Create ICreditCardRepository interface in `backend/src/InvestmentTracker.Domain/Interfaces/ICreditCardRepository.cs`
- [ ] T005 [P] Create IInstallmentRepository interface in `backend/src/InvestmentTracker.Domain/Interfaces/IInstallmentRepository.cs`

---

## Phase 2: Foundational (Database Entities & Migration)

**Purpose**: Core entities and database migration that MUST be complete before ANY user story

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T006 [P] Create FixedDeposit entity in `backend/src/InvestmentTracker.Domain/Entities/FixedDeposit.cs`
- [ ] T007 [P] Create CreditCard entity in `backend/src/InvestmentTracker.Domain/Entities/CreditCard.cs`
- [ ] T008 [P] Create Installment entity in `backend/src/InvestmentTracker.Domain/Entities/Installment.cs`
- [ ] T009 [P] Create FixedDepositConfiguration in `backend/src/InvestmentTracker.Infrastructure/Persistence/Configurations/FixedDepositConfiguration.cs`
- [ ] T010 [P] Create CreditCardConfiguration in `backend/src/InvestmentTracker.Infrastructure/Persistence/Configurations/CreditCardConfiguration.cs`
- [ ] T011 [P] Create InstallmentConfiguration in `backend/src/InvestmentTracker.Infrastructure/Persistence/Configurations/InstallmentConfiguration.cs`
- [ ] T012 Add DbSet properties for FixedDeposit, CreditCard, Installment in `backend/src/InvestmentTracker.Infrastructure/Persistence/AppDbContext.cs`
- [ ] T013 Create and apply EF Core migration `AddFixedDepositAndInstallment`

**Checkpoint**: Database ready - user story implementation can now begin

---

## Phase 3: User Story 4 - Manage Credit Cards (Priority: P3) 🔧 Foundation for US3

**Goal**: Create and manage credit card accounts as containers for installments

**Independent Test**: Create a credit card, view it in the list, deactivate it

**Note**: Implemented before US3 because installments depend on credit cards existing

### Backend for User Story 4

- [ ] T014 [P] [US4] Create CreditCardRepository in `backend/src/InvestmentTracker.Infrastructure/Repositories/CreditCardRepository.cs`
- [ ] T015 [P] [US4] Create CreditCardDto and related request/response classes in `backend/src/InvestmentTracker.Application/DTOs/CreditCardDto.cs`
- [ ] T016 [US4] Create GetCreditCardsUseCase in `backend/src/InvestmentTracker.Application/UseCases/CreditCards/GetCreditCardsUseCase.cs`
- [ ] T017 [P] [US4] Create GetCreditCardUseCase in `backend/src/InvestmentTracker.Application/UseCases/CreditCards/GetCreditCardUseCase.cs`
- [ ] T018 [P] [US4] Create CreateCreditCardUseCase in `backend/src/InvestmentTracker.Application/UseCases/CreditCards/CreateCreditCardUseCase.cs`
- [ ] T019 [P] [US4] Create UpdateCreditCardUseCase in `backend/src/InvestmentTracker.Application/UseCases/CreditCards/UpdateCreditCardUseCase.cs`
- [ ] T020 [P] [US4] Create DeactivateCreditCardUseCase in `backend/src/InvestmentTracker.Application/UseCases/CreditCards/DeactivateCreditCardUseCase.cs`
- [ ] T021 [US4] Create CreditCardsController in `backend/src/InvestmentTracker.API/Controllers/CreditCardsController.cs`
- [ ] T022 [US4] Register CreditCard services in DI container in `backend/src/InvestmentTracker.API/Program.cs`

### Frontend for User Story 4

- [ ] T023 [P] [US4] Create credit-cards feature folder structure and types in `frontend/src/features/credit-cards/types/index.ts`
- [ ] T024 [P] [US4] Create creditCardsApi in `frontend/src/features/credit-cards/api/creditCardsApi.ts`
- [ ] T025 [US4] Create useCreditCards hook in `frontend/src/features/credit-cards/hooks/useCreditCards.ts`
- [ ] T026 [P] [US4] Create CreditCardForm component in `frontend/src/features/credit-cards/components/CreditCardForm.tsx`
- [ ] T027 [P] [US4] Create CreditCardList component in `frontend/src/features/credit-cards/components/CreditCardList.tsx`
- [ ] T028 [US4] Create CreditCardsPage and add route in `frontend/src/pages/CreditCardsPage.tsx`
- [ ] T029 [US4] Add navigation menu item for Credit Cards

**Checkpoint**: Credit card CRUD complete - can create cards to hold installments

---

## Phase 4: User Story 2 - Create and Manage Fixed Deposits (Priority: P2)

**Goal**: Record fixed deposits with terms and maturity dates, track when funds become available

**Independent Test**: Create a fixed deposit, view list with days remaining, close matured deposit

### Backend for User Story 2

- [ ] T030 [P] [US2] Create FixedDepositRepository in `backend/src/InvestmentTracker.Infrastructure/Repositories/FixedDepositRepository.cs`
- [ ] T031 [P] [US2] Create FixedDepositDto and related request/response classes in `backend/src/InvestmentTracker.Application/DTOs/FixedDepositDto.cs`
- [ ] T032 [US2] Create GetFixedDepositsUseCase in `backend/src/InvestmentTracker.Application/UseCases/FixedDeposits/GetFixedDepositsUseCase.cs`
- [ ] T033 [P] [US2] Create GetFixedDepositUseCase in `backend/src/InvestmentTracker.Application/UseCases/FixedDeposits/GetFixedDepositUseCase.cs`
- [ ] T034 [P] [US2] Create CreateFixedDepositUseCase in `backend/src/InvestmentTracker.Application/UseCases/FixedDeposits/CreateFixedDepositUseCase.cs`
- [ ] T035 [P] [US2] Create UpdateFixedDepositUseCase in `backend/src/InvestmentTracker.Application/UseCases/FixedDeposits/UpdateFixedDepositUseCase.cs`
- [ ] T036 [P] [US2] Create CloseFixedDepositUseCase in `backend/src/InvestmentTracker.Application/UseCases/FixedDeposits/CloseFixedDepositUseCase.cs`
- [ ] T037 [US2] Create FixedDepositsController in `backend/src/InvestmentTracker.API/Controllers/FixedDepositsController.cs`
- [ ] T038 [US2] Register FixedDeposit services in DI container in `backend/src/InvestmentTracker.API/Program.cs`

### Frontend for User Story 2

- [ ] T039 [P] [US2] Create fixed-deposits feature folder structure and types in `frontend/src/features/fixed-deposits/types/index.ts`
- [ ] T040 [P] [US2] Create fixedDepositsApi in `frontend/src/features/fixed-deposits/api/fixedDepositsApi.ts`
- [ ] T041 [US2] Create useFixedDeposits hook in `frontend/src/features/fixed-deposits/hooks/useFixedDeposits.ts`
- [ ] T042 [P] [US2] Create FixedDepositForm component in `frontend/src/features/fixed-deposits/components/FixedDepositForm.tsx`
- [ ] T043 [P] [US2] Create FixedDepositCard component in `frontend/src/features/fixed-deposits/components/FixedDepositCard.tsx`
- [ ] T044 [P] [US2] Create FixedDepositList component in `frontend/src/features/fixed-deposits/components/FixedDepositList.tsx`
- [ ] T045 [US2] Create FixedDepositsPage and add route in `frontend/src/pages/FixedDepositsPage.tsx`
- [ ] T046 [US2] Add navigation menu item for Fixed Deposits

**Checkpoint**: Fixed deposit CRUD complete - can track time deposits independently

---

## Phase 5: User Story 3 - Track Credit Card Installment Purchases (Priority: P2)

**Goal**: Record installment purchases, track remaining payments, record payments

**Independent Test**: Create installment on a card, view unpaid balance, record payment

**Dependency**: Requires User Story 4 (Credit Cards) to be complete

### Backend for User Story 3

- [ ] T047 [P] [US3] Create InstallmentRepository in `backend/src/InvestmentTracker.Infrastructure/Repositories/InstallmentRepository.cs`
- [ ] T048 [P] [US3] Create InstallmentDto and related request/response classes in `backend/src/InvestmentTracker.Application/DTOs/InstallmentDto.cs`
- [ ] T049 [US3] Create GetInstallmentsUseCase in `backend/src/InvestmentTracker.Application/UseCases/Installments/GetInstallmentsUseCase.cs`
- [ ] T050 [P] [US3] Create GetAllUserInstallmentsUseCase in `backend/src/InvestmentTracker.Application/UseCases/Installments/GetAllUserInstallmentsUseCase.cs`
- [ ] T051 [P] [US3] Create CreateInstallmentUseCase in `backend/src/InvestmentTracker.Application/UseCases/Installments/CreateInstallmentUseCase.cs`
- [ ] T052 [P] [US3] Create UpdateInstallmentUseCase in `backend/src/InvestmentTracker.Application/UseCases/Installments/UpdateInstallmentUseCase.cs`
- [ ] T053 [P] [US3] Create RecordPaymentUseCase in `backend/src/InvestmentTracker.Application/UseCases/Installments/RecordPaymentUseCase.cs` - supports single-click recording of one payment (SC-005)
- [ ] T054 [P] [US3] Create PayoffInstallmentUseCase in `backend/src/InvestmentTracker.Application/UseCases/Installments/PayoffInstallmentUseCase.cs`
- [ ] T055 [P] [US3] Create GetUpcomingPaymentsUseCase in `backend/src/InvestmentTracker.Application/UseCases/Installments/GetUpcomingPaymentsUseCase.cs`
- [ ] T056 [US3] Create InstallmentsController in `backend/src/InvestmentTracker.API/Controllers/InstallmentsController.cs`
- [ ] T057 [US3] Register Installment services in DI container in `backend/src/InvestmentTracker.API/Program.cs`

### Frontend for User Story 3

- [ ] T058 [P] [US3] Create installment types in `frontend/src/features/credit-cards/types/installment.ts`
- [ ] T059 [P] [US3] Create installmentsApi in `frontend/src/features/credit-cards/api/installmentsApi.ts`
- [ ] T060 [US3] Create useInstallments hook in `frontend/src/features/credit-cards/hooks/useInstallments.ts`
- [ ] T061 [P] [US3] Create InstallmentForm component in `frontend/src/features/credit-cards/components/InstallmentForm.tsx`
- [ ] T062 [P] [US3] Create InstallmentList component in `frontend/src/features/credit-cards/components/InstallmentList.tsx` - include single-click "Record Payment" button per row (SC-005)
- [ ] T063 [P] [US3] Create UpcomingPayments component in `frontend/src/features/credit-cards/components/UpcomingPayments.tsx`
- [ ] T064 [US3] Integrate InstallmentList into CreditCardDetail page

**Checkpoint**: Installment tracking complete - can record and track all installment purchases

---

## Phase 6: User Story 1 - View Available vs Committed Funds (Priority: P1) 🎯 MVP Value

**Goal**: Display dashboard summary showing total assets split into available and committed funds

**Independent Test**: View dashboard with breakdown of available funds, committed funds (fixed deposits + installments)

**Dependency**: Requires User Story 2 (Fixed Deposits) and User Story 3/4 (Installments/Credit Cards) for full data

### Backend for User Story 1

- [ ] T065 [US1] Create AvailableFundsService in `backend/src/InvestmentTracker.Domain/Services/AvailableFundsService.cs`
- [ ] T065a [US1] Create AvailableFundsServiceTests with 100% coverage for financial calculations in `backend/tests/InvestmentTracker.Domain.Tests/Services/AvailableFundsServiceTests.cs` (Constitution: financial calculation test coverage)
- [ ] T066 [P] [US1] Create AvailableFundsSummaryDto in `backend/src/InvestmentTracker.Application/DTOs/AvailableFundsSummaryDto.cs`
- [ ] T067 [US1] Create GetAvailableFundsSummaryUseCase in `backend/src/InvestmentTracker.Application/UseCases/AvailableFunds/GetAvailableFundsSummaryUseCase.cs`
- [ ] T068 [US1] Add available-funds endpoint to existing controller or create AvailableFundsController in `backend/src/InvestmentTracker.API/Controllers/AvailableFundsController.cs`
- [ ] T069 [US1] Register AvailableFunds services in DI container in `backend/src/InvestmentTracker.API/Program.cs`

### Frontend for User Story 1

- [ ] T070 [P] [US1] Create AvailableFunds types in `frontend/src/features/total-assets/types/availableFunds.ts`
- [ ] T071 [P] [US1] Create availableFundsApi in `frontend/src/features/total-assets/api/availableFundsApi.ts`
- [ ] T072 [US1] Create useAvailableFunds hook in `frontend/src/features/total-assets/hooks/useAvailableFunds.ts`
- [ ] T073 [US1] Create AvailableFundsSummary component in `frontend/src/features/total-assets/components/AvailableFundsSummary.tsx`
- [ ] T074 [US1] Integrate AvailableFundsSummary into dashboard/total-assets page

**Checkpoint**: Core value delivered - users can see true available funds at a glance

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [ ] T075 [P] Validate all API endpoints match contracts in `specs/008-fixed-deposit-installment/contracts/`
- [ ] T076 [P] Add loading states and error handling to all new components
- [ ] T077 [P] Add empty state UI for lists (no fixed deposits, no credit cards, no installments)
- [ ] T078 Verify currency conversion works correctly for foreign currency fixed deposits
- [ ] T079 Run quickstart.md validation checklist
- [ ] T080 Update navigation and ensure consistent styling

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1 (Setup) ─────────────────────────────────────┐
                                                     │
Phase 2 (Foundational) ──────────────────────────────┼── BLOCKS ALL
                                                     │
     ┌───────────────────────────────────────────────┘
     │
     ▼
Phase 3 (US4: Credit Cards) ─────┬─────────────────────────────────┐
                                 │                                  │
                                 ▼                                  │
Phase 4 (US2: Fixed Deposits) ───┼── Can run in parallel           │
                                 │   with Phase 3                   │
                                 │                                  │
                                 ▼                                  │
Phase 5 (US3: Installments) ─────┴── Depends on Phase 3 (US4)      │
                                                                    │
                                                                    ▼
Phase 6 (US1: Available Funds) ─── Depends on Phase 4 + Phase 5    │
                                                                    │
Phase 7 (Polish) ───────────────────────────────────────────────────┘
```

### User Story Dependencies

| Story | Can Start After | Notes |
|-------|-----------------|-------|
| US4 (Credit Cards) | Phase 2 | Foundation for US3 |
| US2 (Fixed Deposits) | Phase 2 | Independent, can parallel with US4 |
| US3 (Installments) | US4 complete | Needs credit cards to exist |
| US1 (Available Funds) | US2 + US3 complete | Aggregates data from both |

### Parallel Opportunities Per Phase

**Phase 1**: All tasks T001-T005 can run in parallel

**Phase 2**: T006-T011 can run in parallel, then T012-T013 sequentially

**Phase 3 (US4)**:
- Backend: T014-T015 parallel, then T016-T020 parallel, then T021-T022 sequential
- Frontend: T023-T024 parallel, then T025-T029 as shown

**Phase 4 (US2)**:
- Backend: T030-T031 parallel, then T032-T036 parallel, then T037-T038 sequential
- Frontend: T039-T040 parallel, then T041-T046 as shown

**Phase 5 (US3)**:
- Backend: T047-T048 parallel, then T049-T055 parallel, then T056-T057 sequential
- Frontend: T058-T059 parallel, then T060-T064 as shown

**Phase 6 (US1)**: T066 + T070-T071 parallel, rest sequential

---

## Parallel Example: Phase 4 (User Story 2)

```bash
# Launch backend foundation tasks in parallel:
Task: "Create FixedDepositRepository in backend/.../FixedDepositRepository.cs"
Task: "Create FixedDepositDto in backend/.../FixedDepositDto.cs"

# Then launch use cases in parallel:
Task: "Create GetFixedDepositUseCase in backend/.../GetFixedDepositUseCase.cs"
Task: "Create CreateFixedDepositUseCase in backend/.../CreateFixedDepositUseCase.cs"
Task: "Create UpdateFixedDepositUseCase in backend/.../UpdateFixedDepositUseCase.cs"
Task: "Create CloseFixedDepositUseCase in backend/.../CloseFixedDepositUseCase.cs"

# Launch frontend foundation tasks in parallel:
Task: "Create fixed-deposits types in frontend/.../types/index.ts"
Task: "Create fixedDepositsApi in frontend/.../api/fixedDepositsApi.ts"

# Then launch components in parallel:
Task: "Create FixedDepositForm component in frontend/.../FixedDepositForm.tsx"
Task: "Create FixedDepositCard component in frontend/.../FixedDepositCard.tsx"
Task: "Create FixedDepositList component in frontend/.../FixedDepositList.tsx"
```

---

## Implementation Strategy

### MVP First (Phases 1-6)

1. Complete Phase 1: Setup (enums, interfaces)
2. Complete Phase 2: Foundational (entities, migration)
3. Complete Phase 3: US4 Credit Cards (enables US3)
4. Complete Phase 4: US2 Fixed Deposits (parallel with Phase 3)
5. Complete Phase 5: US3 Installments
6. Complete Phase 6: US1 Available Funds Summary
7. **STOP and VALIDATE**: Full feature working end-to-end
8. Polish in Phase 7

### Incremental Delivery

| After Phase | Deliverable | Value |
|-------------|-------------|-------|
| Phase 3 | Credit card management | Foundation ready |
| Phase 4 | Fixed deposit tracking | Track locked funds |
| Phase 5 | Installment tracking | Track future obligations |
| Phase 6 | Available funds summary | **CORE VALUE**: True liquidity visibility |
| Phase 7 | Polish | Production ready |

---

## Notes

- [P] tasks = different files, no dependencies within the phase
- [Story] label maps task to specific user story for traceability
- US4 implemented before US3 despite lower priority (US3 depends on US4)
- US1 is P1 priority but implemented last (aggregates data from US2/US3/US4)
- Each story should be independently completable and testable after its dependencies
- Commit after each task or logical group
