# Quickstart: Unified Broker Statement Import

## Goal
Validate that a user can import both legacy stock CSV and broker statement CSV from the same entry, handle unresolved symbols, and resolve insufficient-balance buy rows with manual-create parity.

## Prerequisites
- Backend and frontend services are running locally.
- Test user has at least one portfolio and bound currency ledger.
- Sample files are available:
  - legacy stock CSV (existing format)
  - `證券app匯出範例.csv` (broker statement sample)

## Scenario A: Broker Statement Preview + Execute

1. Open portfolio page and click existing stock import entry.
2. Upload `證券app匯出範例.csv`.
3. Confirm system detects `broker_statement`; override format manually once to verify switch behavior.
4. Verify preview rows include normalized fields (date, side, ticker or unresolved, quantity, price, fees/taxes, net settlement).
5. For rows with unresolved security names:
   - Trigger on-demand sync.
   - If still unresolved, input ticker manually per row in listed order.
6. Continue to execution stage.
7. For buy rows with shortfall, choose:
   - Margin OR
   - Top-up + valid top-up transaction type.
8. Execute import.
9. Verify result summary and row-level results are returned.
10. Verify successful rows appear in transaction list with values matching preview.

## Scenario B: Legacy CSV Regression

1. Upload a known-valid legacy stock CSV through the same entry.
2. Verify detection or manual override can keep format as `legacy_csv`.
3. Verify preview and execution succeed without requiring format migration.
4. Validate existing expected outcomes remain unchanged.

## Scenario C: Failure-path Coverage

1. Use a broker file with malformed numeric/header values.
2. Verify row-level diagnostics include `rowNumber`, `fieldName`, `errorCode`, and correction guidance.
3. Simulate sync failure for unresolved names.
4. Verify resolvable rows can still proceed while unresolved rows require manual ticker input.
5. Attempt execute with unresolved shortfall decision.
6. Verify affected rows are blocked with explicit failure reason.

## Scenario D: End-to-End Performance Verification (New Account → Broker CSV → Performance)

### Fixed Inputs
- Sample CSV: `/workspaces/InvestmentTracker/證券app匯出範例.csv`
- CSV fixed facts for this scenario: `50` data rows, years include `2025` and `2026`.
- Expected import format: `broker_statement`.

### Reproducible Checklist (fixed steps + fixed assertions)

1. **Create a brand-new account** (UI: `註冊`; fields `顯示名稱`/`電子郵件`/`密碼`; button `建立帳號`) or API `POST /api/auth/register`.
   - Request keys: `email`, `password`, `displayName`.
   - Must pass: HTTP `201`; response `accessToken`, `refreshToken`, `expiresAt`, `user.id`, `user.email`, `user.displayName` are non-null.

2. **Get target portfolio** via `GET /api/portfolios` (or UI portfolio selector).
   - Must pass: at least one portfolio with `baseCurrency = "TWD"`; selected `id` and `boundCurrencyLedgerId` are non-null.

3. **Open stock import flow** (UI button `匯入`, modal title `匯入股票交易`).
   - Must pass: import type selector (`匯入類型`) is visible and supports values `券商` (`broker_statement`) and `一般` (`legacy_csv`).

4. **Upload sample CSV and generate preview** (`產生預覽` / `重新預覽`) with `selectedFormat = broker_statement`.
   - API contract: `POST /api/stocktransactions/import/preview` with `portfolioId`, `csvContent`, `selectedFormat`.
   - Must pass:
     - HTTP `200`, `sessionId` non-null UUID.
     - `selectedFormat == "broker_statement"`.
     - `summary.totalRows == 50`.
     - `summary.totalRows == summary.validRows + summary.requiresActionRows + summary.invalidRows`.
     - `rows.length == 50`.

5. **Verify preview row contracts (row table + diagnostics)**.
   - UI table columns map to API fields:
     - `列號` -> `rows[].rowNumber`
     - `標的名稱` -> `rows[].rawSecurityName`
     - `股票代號` -> `rows[].ticker` (or manual input)
     - `買賣方向` -> `rows[].tradeSide` / `rows[].confirmedTradeSide`
     - `餘額不足處理` -> `rows[].balanceDecision` + action selectors
     - `狀態` -> `rows[].status`
   - Must pass:
     - Every row has non-null `rowNumber`, `tradeSide`, `status`, `actionsRequired` (array), `fees`, `taxes`.
     - If diagnostics exist, each `errors[]` item has non-null `rowNumber`, `fieldName`, `errorCode`, `message`, `correctionGuidance`.

6. **Resolve required actions and execute import** (UI `確認匯入` or API `POST /api/stocktransactions/import/execute`).
   - Request keys: `sessionId`, `portfolioId`, `rows[]` (`rowNumber`, `ticker`, `confirmedTradeSide`, `exclude`, optional `balanceAction`, optional `topUpTransactionType`), optional `defaultBalanceAction`.
   - Must pass:
     - `summary.totalRows == summary.insertedRows + summary.failedRows`.
     - `summary.totalRows == results.length`.
     - `summary.errorCount == errors.length`.
     - For each `results[i]`:
       - If `success == true`, `transactionId` must be non-null and `errorCode` must be null.
       - If `success == false`, `transactionId` must be null and `errorCode` must be non-null.

7. **Verify imported transactions exist** using `GET /api/stocktransactions?portfolioId={id}` (or portfolio transaction list UI).
   - Must pass: total rows increased by at least `summary.insertedRows`; imported data includes trade dates in `2025`/`2026` and non-empty `ticker` for successful rows.

8. **Verify performance availability and annual result**.
   - API step A: `GET /api/portfolios/{portfolioId}/performance/years`.
   - Must pass A: `years` contains both `2025` and `2026`; `currentYear` non-null.
   - API step B: `POST /api/portfolios/{portfolioId}/performance/year` with `year: 2026` (optionally provide `yearEndPrices`/`yearStartPrices` to bypass external price dependency).
   - Must pass B: response `year == 2026`, `transactionCount > 0`, `sourceCurrency` non-null.
   - UI mapping checks on `/performance` (`績效分析` -> `歷史績效`):
     - `資金加權報酬率` uses `modifiedDietzPercentage` (home) / `modifiedDietzPercentageSource` (source).
     - `時間加權報酬率` uses `timeWeightedReturnPercentage` (home) / `timeWeightedReturnPercentageSource` (source).
     - `年度摘要` (`年初價值`/`年末價值`/`淨投入`) maps to `startValue*`/`endValue*`/`netContributions*`.

### Nullability Contract for This Acceptance

| Scope | Field | Rule |
|---|---|---|
| Register response | `accessToken`, `refreshToken`, `expiresAt`, `user.id`, `user.email`, `user.displayName` | MUST be non-null |
| Preview response | `sessionId`, `selectedFormat`, `summary`, `rows` | MUST be non-null |
| Preview row | `rowNumber`, `tradeSide`, `status`, `actionsRequired`, `fees`, `taxes` | MUST be non-null |
| Preview row | `tradeDate`, `rawSecurityName`, `ticker`, `confirmedTradeSide`, `quantity`, `unitPrice`, `netSettlement`, `currency`, `balanceDecision` | NULL allowed |
| Execute response | `status`, `summary`, `results`, `errors` | MUST be non-null |
| Execute result row | `rowNumber`, `success`, `message` | MUST be non-null |
| Execute result row | `transactionId` | Non-null only when `success=true`; otherwise must be null |
| Execute result row | `errorCode` | Non-null only when `success=false`; otherwise must be null |
| Performance response | `year`, `cashFlowCount`, `transactionCount`, `missingPrices`, `isComplete` | MUST be non-null |
| Performance response | `coverageDays`, `hasOpeningBaseline`, `usesPartialHistoryAssumption`, `xirrReliability` | MUST be non-null |
| Performance response | `xirr*`, `totalReturn*`, `modifiedDietz*`, `timeWeightedReturn*`, `startValue*`, `endValue*`, `earliestTransactionDateInYear` | NULL allowed (especially when `isComplete=false`) |
| Performance response | `missingPrices` | Must be empty when `isComplete=true`; may be non-empty when `isComplete=false` |

### Failure Criteria (any one = FAIL)

- Any required endpoint returns non-2xx (except explicitly expected validation failures during negative checks).
- Preview fixed assertions fail (`summary.totalRows != 50`, `rows.length != 50`, or summary arithmetic mismatch).
- Execute arithmetic mismatch (`totalRows != insertedRows + failedRows`, `totalRows != results.length`, or `errorCount != errors.length`).
- Row-level contract violation (`success=true` with null `transactionId`, or `success=false` with null `errorCode`).
- Performance signal contract violation (`coverageDays` is null/negative, `hasOpeningBaseline` is null, `usesPartialHistoryAssumption` is null, or `xirrReliability` is null).
- `performance/years` does not include `2025` and `2026` after successful import.
- `performance/year` for `2026` returns `transactionCount == 0` after successful import.
- UI/API binding mismatch for performance cards (`資金加權報酬率`, `時間加權報酬率`, `年度摘要`) versus response field mapping above.

### Traceability Addendum (T051-T063)

| Task Range | Covered Scope | Tests / Evidence |
|---|---|---|
| T051-T054 | Import baseline request/snapshot contracts + yearly performance signal fields (`coverageDays`, `hasOpeningBaseline`, `usesPartialHistoryAssumption`, `xirrReliability`) | Backend suites: `StockTransactionsImportControllerTests`, `HistoricalPerformanceServiceReturnTests` (see Group G execution log, PASS) |
| T059 | API contract assertions for baseline/coverage signal fields | `dotnet test ... InvestmentTracker.API.Tests.csproj --filter "FullyQualifiedName~StockTransactionsImportControllerTests"` (Group G log: PASS) |
| T060, T062 | Import UI/API consistency and import-regression alignment | `src/test/stock-import.broker-preview.test.tsx`, `src/test/stock-import.balance-action.test.tsx`, `src/test/stock-import.legacy-regression.test.tsx` (Group E log: PASS) |
| T061, T063 | Performance reliability/coverage behavior and performance-regression alignment | `src/test/performance.metrics-binding.test.tsx`, `src/test/useHistoricalPerformance.test.ts`, `src/test/dashboard.aggregate-fixed.test.tsx` (Group E/Group G logs: PASS) |

## Verification Notes (T039 Update)

- Updated after implementation with concrete automated evidence (date: **2026-02-16**).
- Evidence below maps Scenario A/B/C to executed test suites for **US1-US3** and polish items.
- Coverage in this run:
  - US1: dual-format import entry, detection/override, broker preview normalization, unresolved-symbol sync path, ambiguous-side confirmation.
  - US2: insufficient-balance decisions (global + per-row), Top-up validation, unresolved decision blocking.
  - US3: legacy CSV no-regression preview/execute behavior and row-level error mapping continuity.
  - Polish: row-level error-code diagnostics behavior and 500-row broker preview performance guard.
- Not directly executed in this run:
  - OpenAPI UI/manual inspection for annotation rendering (polish T038) should be validated via Swagger UI separately.

## Verification Evidence (T040 Execution Log)

### Commands Executed

```bash
dotnet test "/workspaces/InvestmentTracker/backend/tests/InvestmentTracker.API.Tests/InvestmentTracker.API.Tests.csproj" --filter "FullyQualifiedName~StockTransactionsImportControllerTests|FullyQualifiedName~MarketDataControllerTwseSyncTests|FullyQualifiedName~StockTransactionsLegacyImportRegressionTests"

dotnet test "/workspaces/InvestmentTracker/backend/tests/InvestmentTracker.Application.Tests/InvestmentTracker.Application.Tests.csproj" --filter "FullyQualifiedName~ExecuteStockImportBalanceActionTests|FullyQualifiedName~PreviewStockImportPerformanceTests"

npm --prefix "/workspaces/InvestmentTracker/frontend" run test:run -- src/test/stock-import.broker-preview.test.tsx src/test/stock-import.balance-action.test.tsx src/test/stock-import.legacy-regression.test.tsx
```

### Command Outcome Summary

| Command Scope | Result | Output Evidence |
|---|---|---|
| Backend API import/sync regression | PASS | Failed: 0, Passed: 13, Total: 13, Duration: 5s |
| Backend application balance/performance | PASS | Failed: 0, Passed: 14, Total: 14, Duration: 47ms |
| Frontend import interaction regression | PASS | 3 test files passed, 9 tests passed, 0 failed |

### Scenario-to-Evidence Mapping

#### Scenario A (Broker Statement Preview + Execute) — Verified

- **Format detection + manual override** verified by frontend test:
  - `src/test/stock-import.broker-preview.test.tsx`
  - `broker format detection can be manually overridden before re-preview`
- **Preview/execute API contract + key value parity (date/quantity/price/fees)** verified by backend API tests:
  - `StockTransactionsImportControllerTests.Preview_Endpoint_IsAvailable_AndReturnsContractFields`
  - `StockTransactionsImportControllerTests.PreviewThenExecute_PreservesPreviewValues_ForDateQuantityPriceFees_AndUsesConfirmedTradeSide`
- **Ambiguous side requires confirmation before execute** verified by:
  - `StockTransactionsImportControllerTests.Execute_UsesConfirmedTradeSideInResultRowContract_WhenRejected`
  - frontend test `ambiguous-side rows require per-row confirmation and submit confirmed side in execute payload`
- **Unresolved symbol sync path (per unresolved row attempted)** verified by backend API tests:
  - `MarketDataControllerTwseSyncTests.SyncOnDemand_ReturnsContract_AndPerUnresolvedRowSyncAttemptAssertions`
  - `MarketDataControllerTwseSyncTests.SyncOnDemand_NormalizesDistinctInputNames_ForRequestedCount_AndProducesOneOutcomePerCanonicalName`
- **Row ordering stability for unresolved/user-action rows** verified by frontend test:
  - `preview row ordering remains stable after per-row confirmation interaction`

#### Scenario B (Legacy CSV Regression) — Verified

- **Legacy preview/execute remains functional in unified entry** verified by:
  - `StockTransactionsLegacyImportRegressionTests.LegacyPreviewThenExecute_PreservesKeyValues_AndCommitsSuccessfully`
  - frontend test `legacy format still supports preview and execute through unified import flow`
- **Manual override precedence retained** verified by:
  - `StockTransactionsLegacyImportRegressionTests.Preview_UsesManualBrokerOverride_OverDetectedLegacyFormat`
  - `StockTransactionsLegacyImportRegressionTests.Preview_UsesManualLegacyOverride_OverDetectedBrokerFormat`
- **Row-level error mapping continuity** verified by frontend test:
  - `legacy execute result preserves row-level error mapping assumptions`

#### Scenario C (Failure-path Coverage) — Verified

- **Malformed header/field diagnostics surfaced with row + field context** verified by:
  - frontend test `manual broker override on detected legacy CSV surfaces header error and allows switching back to legacy format` (checks `CSV_HEADER_MISSING`, row label, field label)
  - frontend legacy regression test checks row-level diagnostics content (`rowNumber`, `fieldName`, `errorCode`, `correctionGuidance`)
- **Sync unresolved/failure path returns deterministic unresolved outcomes** verified by:
  - `MarketDataControllerTwseSyncTests.SyncOnDemand_ReturnsContract_AndPerUnresolvedRowSyncAttemptAssertions` (`requested=2`, `resolved=0`, `unresolved=2`, one unresolved error per row in test assertions)
- **Insufficient-balance unresolved decisions are blocked with explicit reason** verified by:
  - `ExecuteStockImportBalanceActionTests.ExecuteAsync_BuyShortfall_WithNoneDecision_ShouldReturnBalanceActionRequired`
  - `ExecuteStockImportBalanceActionTests.ExecuteAsync_BuyShortfall_WithTopUpWithoutType_ShouldReturnBalanceActionRequired`
  - frontend test `blocks execute when TopUp lacks topUpTransactionType and enables after selection`
- **Additional explicit failure reason behavior** verified by:
  - `ExecuteStockImportBalanceActionTests.ExecuteAsync_RowNotInSession_ShouldReturnSessionRowMismatchErrorCode`
  - `StockTransactionsImportControllerTests.Execute_ReturnsRejectedWithSessionRowMismatch_WhenRowIsNotInPreviewSession`

### US/Polish Traceability Snapshot

| Scope | Verification Outcome |
|---|---|
| US1 | PASS (broker format detection/override, preview normalization contract, unresolved sync behavior, ambiguous-side confirmation gating) |
| US2 | PASS (balance action required, Top-up type validation, global/per-row decision payload behavior) |
| US3 | PASS (legacy preview/execute and error mapping regressions covered) |
| Polish T037 (error codes/diagnostics behavior) | PASS (row-level `errorCode` diagnostics validated in backend/frontend regression tests) |
| Polish T041 (500-row preview performance <=3s median) | PASS (`PreviewStockImportPerformanceTests.ExecuteAsync_BrokerPreview500Rows_MedianElapsedShouldBeWithin3Seconds`) |
| Polish T038 (OpenAPI annotations visibility) | Not executed in this run (manual Swagger inspection required) |

## Recorded Verification Artifacts

- Automated test command logs (pass summaries listed above).
- Scenario-to-test traceability entries for quick reproduction.
- For manual evidence collection (optional extension): UI screenshots and backend runtime logs can still be attached per team QA process.

## Verification Notes (Group D Reliability Update)

- Updated for the reliability fix cycle with concrete automated evidence (date: **2026-02-18**).
- Root cause clarifications:
  - Position cards intentionally render only net-positive holdings (`totalShares > 0`); this is expected summary semantics, not silent import-row drops.
- Performance loading behavior:
  - Current-year benchmark flow now falls back to benchmark-returns API when YTD fetch is unavailable, preventing a persistent loading spinner.
- External call reduction:
  - Repeated `missingPrices` tickers are deduplicated before quote fetching.
  - Quote cache lookup keeps market-aware keys with legacy-key fallback for backward-compatible cache reuse.
- Backend fixture hardening:
  - Broker sample CSV is resolved from test output (`AppContext.BaseDirectory`) to keep tests independent from repository-relative paths.
- Test-infra scope note:
  - Playwright infrastructure is not configured in this repository; this scenario is covered by backend API integration tests plus frontend integration tests.

## Verification Evidence (Group D Execution Log)

### Commands Executed

```bash
dotnet test "/workspaces/InvestmentTracker/backend/tests/InvestmentTracker.API.Tests/InvestmentTracker.API.Tests.csproj" --filter "FullyQualifiedName~StockTransactionsImportControllerTests"

npm --prefix "/workspaces/InvestmentTracker/frontend" run test:run -- src/test/performance.metrics-binding.test.tsx src/test/portfolio.page.non-transaction-cache.test.tsx src/test/stock-import.broker-preview.test.tsx
```

### Command Outcome Summary

| Command Scope | Result | Output Evidence |
|---|---|---|
| Backend API import/performance contract regression | PASS | Failed: 0, Passed: 15, Total: 15 |
| Frontend integration reliability regression | PASS | 3 test files passed, 33 tests passed, 0 failed |

### Reliability Fix-Cycle Traceability (2026-02-18)

- **(a) Net-positive holdings semantics clarification**:
  - `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsImportControllerTests.cs` (`RegisterPreviewExecuteAndPerformance_UsingSampleCsv_ShouldProduceValidEndToEndData`)
  - `frontend/src/pages/Portfolio.tsx`
  - `frontend/src/test/portfolio.page.non-transaction-cache.test.tsx`
- **(b) Performance spinner fix via YTD fallback behavior**:
  - `frontend/src/pages/Performance.tsx`
  - `frontend/src/test/performance.metrics-binding.test.tsx` (`falls back to benchmark returns API and unblocks loading when current-year YTD fetch is unavailable`)
- **(c) Reduced duplicate external calls via missing-ticker dedupe and cache-key compatibility**:
  - `frontend/src/pages/Performance.tsx`
  - `frontend/src/test/performance.metrics-binding.test.tsx` (`deduplicates repeated missing tickers...`, `uses market-aware quote cache key first...`, `falls back to legacy quote cache key...`)
  - `frontend/src/test/portfolio.page.non-transaction-cache.test.tsx`
- **(d) Backend fixture hardening for sample CSV path independence**:
  - `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsImportControllerTests.cs`
  - `backend/tests/InvestmentTracker.API.Tests/InvestmentTracker.API.Tests.csproj`
- **(e) Scenario coverage without Playwright infra**:
  - Frontend integration: `frontend/src/test/stock-import.broker-preview.test.tsx` (broker import to performance binding flow)
  - Backend integration: `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsImportControllerTests.cs`

## Verification Notes (Group E Closure Update)

- Updated for Group E Speckit closure with concrete frontend QA evidence (date: **2026-02-18**).
- Root cause summary:
  - **(A) Import UI/API contract alignment gap**: import payload/result field usage needed to stay synchronized across `types`, `StockImportButton`, and `CSVImportModal`.
  - **(B) Performance reliability and metric-binding consistency gap**: current-year fallback and metric card binding behavior needed consistent handling in `Performance.tsx`.
  - **(C) Import regression alignment gap**: import-focused regression tests needed to match the latest contract/gating behavior.
  - **(D) Performance regression alignment gap**: performance-focused regression tests needed to match dedupe/cache compatibility and reliability behavior.
- QA note:
  - Expected fallback stderr emitted by fallback-path test scaffolding is non-blocking when all assertions pass.

## Verification Evidence (Group E Execution Log)

### Commands Executed

```bash
npm --prefix "/workspaces/InvestmentTracker/frontend" run type-check

npm --prefix "/workspaces/InvestmentTracker/frontend" run build

npm --prefix "/workspaces/InvestmentTracker/frontend" run test:run -- src/test/stock-import.broker-preview.test.tsx src/test/stock-import.balance-action.test.tsx src/test/stock-import.legacy-regression.test.tsx src/test/performance.metrics-binding.test.tsx src/test/useHistoricalPerformance.test.ts
```

### Command Outcome Summary

| Command Scope | Result | Output Evidence |
|---|---|---|
| Frontend type-check | PASS | Type-check completed without errors |
| Frontend production build | PASS | Build completed successfully |
| Frontend import/performance regression suite | PASS | 5 test files passed, 60 tests passed, 0 failed |

### Code-Review Resolution Notes

- Market-aware quote cache handling is aligned to write market-aware cache entries while preserving legacy-key fallback compatibility.
- Conditional TopUp gating is aligned: execute is blocked only when `balanceAction` is `TopUp` and `topUpTransactionType` is missing.

### Group E Traceability (Frontend + Tests)

- **A. Import UI/API contract alignment**
  - `frontend/src/types/index.ts`
  - `frontend/src/components/import/StockImportButton.tsx`
  - `frontend/src/components/import/CSVImportModal.tsx`
  - Tests:
    - `frontend/src/test/stock-import.broker-preview.test.tsx`
    - `frontend/src/test/stock-import.balance-action.test.tsx`
    - `frontend/src/test/stock-import.legacy-regression.test.tsx`
- **B. Performance reliability and metric-binding consistency**
  - `frontend/src/pages/Performance.tsx`
  - Tests:
    - `frontend/src/test/performance.metrics-binding.test.tsx`
    - `frontend/src/test/useHistoricalPerformance.test.ts`
- **C. Import regression tests alignment scope**
  - `frontend/src/test/stock-import.broker-preview.test.tsx`
  - `frontend/src/test/stock-import.balance-action.test.tsx`
  - `frontend/src/test/stock-import.legacy-regression.test.tsx`
- **D. Performance regression tests alignment scope**
  - `frontend/src/test/performance.metrics-binding.test.tsx`
  - `frontend/src/test/useHistoricalPerformance.test.ts`

## Verification Notes (Group F Backend Closure Update)

- Updated for Group F Speckit closure with backend QA evidence (date: **2026-02-19**).
- Root cause:
  - Broker CSV rows may come in newest-to-oldest order.
  - Previous execution order could process a same-day buy before the corresponding sell cash inflow, causing premature TopUp and over-topup risk.
- Fix strategy:
  - Enforce deterministic execute ordering: `tradeDate` ascending -> `side` priority (`sell` before `buy` on same date) -> `rowNumber` ascending.
  - Keep same-day behavior deterministic and reproducible across runs.
- Additional guard from code review:
  - Reject execute requests with duplicated `rows[].rowNumber` early with a clear validation error, preventing ambiguous row mapping.

## Verification Evidence (Group F Execution Log)

### Commands Executed

```bash
dotnet build "/workspaces/InvestmentTracker/backend/src/InvestmentTracker.API/InvestmentTracker.API.csproj"

dotnet test "/workspaces/InvestmentTracker/backend/tests/InvestmentTracker.Application.Tests/InvestmentTracker.Application.Tests.csproj" --filter "FullyQualifiedName~ExecuteStockImportBalanceActionTests"

dotnet test "/workspaces/InvestmentTracker/backend/tests/InvestmentTracker.API.Tests/InvestmentTracker.API.Tests.csproj" --filter "FullyQualifiedName~StockTransactionsImportControllerTests"
```

### Command Outcome Summary

| Command Scope | Result | Outcome |
|---|---|---|
| Backend build | PASS | Build completed successfully. |
| Backend application regression | PASS | `ExecuteStockImportBalanceActionTests` passed, including same-day and reverse-chronological no-over-topup scenarios. |
| Backend API regression | PASS | `StockTransactionsImportControllerTests` passed, including same-day and reverse-chronological no-over-topup scenarios plus duplicate-rowNumber bad-request coverage. |

### Group F Traceability (Backend Files + Key Tests)

- `backend/src/InvestmentTracker.Application/UseCases/StockTransactions/ExecuteStockImportUseCase.cs`
  - Execute ordering and deterministic tie-breaker implementation (`tradeDate` -> `side` -> `rowNumber`).
  - Duplicate `rows[].rowNumber` guard before session/transaction processing.
- `backend/tests/InvestmentTracker.Application.Tests/UseCases/StockTransactions/ExecuteStockImportBalanceActionTests.cs`
  - `ExecuteAsync_SameDayBuyBeforeSell_WithBuyRowNumberSmaller_ShouldUseSellIncomeWithoutTopUp`
  - `ExecuteAsync_CsvNewestToOldest_SellEarlierThanBuy_ShouldUseSellIncomeWithoutTopUp`
  - `ExecuteAsync_RowsContainDuplicateRowNumber_ShouldThrowBusinessRuleException`
- `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsImportControllerTests.cs`
  - `Execute_SameDayBuyFirstSellLater_WithBuyRowNumberSmaller_ShouldNotCreateTopUpDeposit`
  - `Execute_ReverseChronologicalSellThenBuyPairs_ShouldCommitWithoutTopUpForKeyRows`
  - `Execute_ReturnsBadRequest_WhenRowsContainDuplicateRowNumber`

## Verification Notes (Group G Reliability & Pricing Policy Update)

- Updated for Group G reliability and pricing-policy closure with automated QA evidence (date: **2026-02-19**).
- 2025 Modified Dietz `2070.01%` root cause is reproduced and localized:
  - Core trigger: no opening baseline + very-late top-up creates a near-zero weighted denominator in Modified Dietz.
  - UI-equivalent validation path is covered by import-to-performance flow (`匯入股票交易` preview/execute -> `績效分析` annual view for 2025).
- Historical price policy is verified as:
  - **Yahoo-first** for historical prices.
  - **Stooq fallback only for US/UK** contexts.
  - **No Stooq fallback** for non-US/UK contexts (for example EU/TW cases).
- XIRR rendering behavior is explicitly verified as three states: `計算中` / `低信度` / `不可用`.
- All targeted regression suites and frontend type-check passed in this round.

## Verification Evidence (Group G Execution Log)

### Commands Executed

```bash
dotnet test "/workspaces/InvestmentTracker/backend/tests/InvestmentTracker.Application.Tests/InvestmentTracker.Application.Tests.csproj" --filter "FullyQualifiedName~HistoricalPerformanceServiceReturnTests"

dotnet test "/workspaces/InvestmentTracker/backend/tests/InvestmentTracker.API.Tests/InvestmentTracker.API.Tests.csproj" --filter "FullyQualifiedName~StockTransactionsImportControllerTests"

dotnet test "/workspaces/InvestmentTracker/backend/tests/InvestmentTracker.Infrastructure.Tests/InvestmentTracker.Infrastructure.Tests.csproj" --filter "FullyQualifiedName~HistoricalYearEndDataServiceTests|FullyQualifiedName~MonthlySnapshotServiceTests"

dotnet test "/workspaces/InvestmentTracker/backend/tests/InvestmentTracker.API.Tests/InvestmentTracker.API.Tests.csproj" --filter "FullyQualifiedName~MarketDataControllerHistoricalPriceTests"

npm --prefix "/workspaces/InvestmentTracker/frontend" run test:run -- src/test/dashboard.aggregate-fixed.test.tsx src/test/performance.metrics-binding.test.tsx

npm --prefix "/workspaces/InvestmentTracker/frontend" run type-check
```

### Command Outcome Summary

| Command Scope | Result | Outcome |
|---|---|---|
| Application regression (`HistoricalPerformanceServiceReturnTests`) | PASS | Failed: 0, Passed: 9, Total: 9 |
| API regression (`StockTransactionsImportControllerTests`) | PASS | Failed: 0, Passed: 23, Total: 23 |
| Infrastructure regression (`HistoricalYearEndDataServiceTests` + `MonthlySnapshotServiceTests`) | PASS | Failed: 0, Passed: 40, Total: 40 |
| API regression (`MarketDataControllerHistoricalPriceTests`) | PASS | Failed: 0, Passed: 10, Total: 10 |
| Frontend regression (`dashboard.aggregate-fixed` + `performance.metrics-binding`) | PASS | 2 test files passed, 24 tests passed, 0 failed |
| Frontend type-check | PASS | Type-check completed without errors |

### Group G Traceability (Files + Key Tests)

- **(1) 2025 MD `2070.01%` root cause reproduction/localization (UI-equivalent path)**
  - `backend/tests/InvestmentTracker.Application.Tests/HistoricalPerformanceServiceReturnTests.cs`
    - `CalculateYearPerformanceAsync_NoOpeningBaseline_WithLateTopUpOnly_CanReachExtremeModifiedDietz`
  - `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsImportControllerTests.cs`
    - `PreviewExecuteAndYearPerformance_WithoutOpeningBaselineWithLateTopUp_ShouldExposeMdExtremeRootCause`
  - UI-equivalent route in test flow:
    - stock import preview/execute -> `GET /api/portfolios/{portfolioId}/performance/years` -> `POST /api/portfolios/{portfolioId}/performance/year` (`Year = 2025`)

- **(2) Yahoo-first historical price strategy + Stooq US/UK-only fallback**
  - `backend/src/InvestmentTracker.Infrastructure/Services/MonthlySnapshotService.cs`
    - `GetHistoricalPriceAsync` enforces Yahoo-first and limits Stooq fallback to `US/UK`.
  - `backend/tests/InvestmentTracker.API.Tests/Controllers/MarketDataControllerHistoricalPriceTests.cs`
    - `GetHistoricalPrice_NonYearEnd_YahooFails_UsMarket_FallsBackToStooq`
    - `GetHistoricalPrice_NonYearEnd_YahooFails_UkTicker_FallsBackToStooq`
    - `GetHistoricalPrice_NonYearEnd_YahooFails_NonUsUkMarket_DoesNotFallbackToStooq`
    - `GetHistoricalPrices_NonYearEnd_YahooFails_NonUsUkMarket_DoesNotFallbackToStooq`
  - `backend/tests/InvestmentTracker.Infrastructure.Tests/Services/HistoricalYearEndDataServiceTests.cs`
    - `GetOrFetchYearEndPriceAsync_EuStock_YahooFails_DoesNotFallbackToStooq`
    - `GetOrFetchYearEndPriceAsync_UkMarket_YahooFails_FallsBackToStooq`
  - `backend/tests/InvestmentTracker.Infrastructure.Tests/Services/MonthlySnapshotServiceTests.cs`
    - `GetMonthlyNetWorthAsync_YahooPriceMissing_FallsBackToStooq_WhenMarketIsUS`
    - `GetMonthlyNetWorthAsync_YahooPriceMissing_DoesNotFallbackToStooq_WhenMarketIsEU`

- **(3) XIRR three-state display (`計算中` / `低信度` / `不可用`)**
  - `frontend/src/pages/Dashboard.tsx`
    - `xirrDisplayState` branch logic for loading/lowConfidence/unavailable states.
  - `frontend/src/test/dashboard.aggregate-fixed.test.tsx`
    - `shows explicit loading text while auto-refresh recalculates unavailable XIRR`
    - `shows low confidence label when XIRR period is too short`
    - `shows unavailable label when aggregate XIRR cannot be calculated`
  - `frontend/src/test/performance.metrics-binding.test.tsx`
    - `shows low-confidence XIRR state explicitly when reliability is Low`
    - `shows XIRR loading state when performance values are still incomplete`

- **(4) Regression/QA closure snapshot**
  - Backend application/API/infrastructure targeted suites all PASS.
  - Frontend targeted regressions and type-check PASS.
  - Coverage links root-cause reproduction, pricing-policy constraints, and UI-state behavior in one evidence set.

## Verification Notes (Group H XIRR/Annual Degrade Closure Update)

- Updated for Group H closure with automated QA evidence (date: **2026-02-20**).
- Single-year Performance page no longer renders XIRR as the primary annual value; annual card focus is kept on non-XIRR primary metrics and reliability-aware signals.
- Dashboard and Portfolio XIRR status copy is unified to the same explicit state wording, and Portfolio no longer displays the `-` placeholder for XIRR state.
- The 2025 Modified Dietz extreme-value scenario is treated as a **degraded/low-confidence annual signal** when confidence is low, and is no longer presented as a normal primary annual value.
- Backend and frontend regressions confirm consistency across contract fields, degrade reasons, and UI rendering behavior.

## Verification Evidence (Group H Execution Log)

### Commands Executed

```bash
dotnet test "/workspaces/InvestmentTracker/backend/tests/InvestmentTracker.Application.Tests/InvestmentTracker.Application.Tests.csproj" --filter "FullyQualifiedName~HistoricalPerformanceServiceReturnTests|FullyQualifiedName~CalculateAggregateYearPerformanceUseCaseTests"

dotnet test "/workspaces/InvestmentTracker/backend/tests/InvestmentTracker.API.Tests/InvestmentTracker.API.Tests.csproj" --filter "FullyQualifiedName~StockTransactionsImportControllerTests"

npm --prefix "/workspaces/InvestmentTracker/frontend" run test:run -- src/test/dashboard.aggregate-fixed.test.tsx src/test/portfolio.performance-metrics.test.tsx

npm --prefix "/workspaces/InvestmentTracker/frontend" run test:run -- src/test/performance.metrics-binding.test.tsx src/test/stock-import.broker-preview.test.tsx src/test/useHistoricalPerformance.test.ts

npm --prefix "/workspaces/InvestmentTracker/frontend" run type-check
```

### Command Outcome Summary

| Command Scope | Result | Outcome |
|---|---|---|
| Backend application regression (`HistoricalPerformanceServiceReturnTests` + `CalculateAggregateYearPerformanceUseCaseTests`) | PASS | Failed: 0, Passed: 18, Total: 18 |
| Backend API regression (`StockTransactionsImportControllerTests`) | PASS | Failed: 0, Passed: 23, Total: 23 |
| Frontend regression (`dashboard.aggregate-fixed` + `portfolio.performance-metrics`) | PASS | 2 test files passed, 8 tests passed, 0 failed |
| Frontend regression (`performance.metrics-binding` + `stock-import.broker-preview` + `useHistoricalPerformance`) | PASS | 3 test files passed, 51 tests passed, 0 failed |
| Frontend type-check | PASS | Type-check completed without errors |

### Group H Traceability (T084-T097)

| Task(s) | Key File(s) | Key Test(s) / Evidence |
|---|---|---|
| T084, T094 | `frontend/src/pages/Performance.tsx`, `frontend/src/types/index.ts` | `frontend/src/test/performance.metrics-binding.test.tsx`, `frontend/src/test/useHistoricalPerformance.test.ts`, `frontend/src/test/stock-import.broker-preview.test.tsx` |
| T085 | `frontend/src/test/performance.metrics-binding.test.tsx` | Single-year XIRR disablement + low-confidence degrade-hint assertions |
| T086 | `frontend/src/pages/Dashboard.tsx` | `frontend/src/test/dashboard.aggregate-fixed.test.tsx` |
| T087 | `frontend/src/components/portfolio/PerformanceMetrics.tsx` | `frontend/src/test/portfolio.performance-metrics.test.tsx` |
| T088 | `frontend/src/test/dashboard.aggregate-fixed.test.tsx`, `frontend/src/test/portfolio.performance-metrics.test.tsx` | Dashboard/Portfolio XIRR state regression coverage |
| T089 | `backend/src/InvestmentTracker.Application/DTOs/PerformanceDtos.cs` | API contract assertions in `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsImportControllerTests.cs` |
| T090 | `backend/src/InvestmentTracker.Application/Services/HistoricalPerformanceService.cs` | `backend/tests/InvestmentTracker.Application.Tests/HistoricalPerformanceServiceReturnTests.cs` |
| T091 | `backend/src/InvestmentTracker.Application/UseCases/Performance/CalculateAggregateYearPerformanceUseCase.cs` | `backend/tests/InvestmentTracker.Application.Tests/UseCases/CalculateAggregateYearPerformanceUseCaseTests.cs` |
| T092 | `backend/tests/InvestmentTracker.Application.Tests/HistoricalPerformanceServiceReturnTests.cs`, `backend/tests/InvestmentTracker.Application.Tests/UseCases/CalculateAggregateYearPerformanceUseCaseTests.cs` | Degrade-reason branch regressions (18 passed summary scope) |
| T093 | `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsImportControllerTests.cs` | Annual degrade-signal contract API regressions (23 passed summary scope) |
| T095 | `frontend/src/test/stock-import.broker-preview.test.tsx` | Import-to-performance degraded-summary regression |
| T096 | `specs/012-import-broker-statement/tasks.md` | Group H checklist completion recorded |
| T097 | `specs/012-import-broker-statement/quickstart.md` | This Group H notes/evidence/traceability update |

## Verification Notes (Group I UX Copy & Import Preview Usability Update)

- Updated for Group I UX copy and import-preview usability closure with automated QA evidence (date: **2026-02-20**).
- This round copy/tooltip/modal adjustments focus on readability and operator confidence:
  - Annual low-confidence wording now explicitly describes confidence impact for MD/TWR/XIRR indicators instead of generic disablement wording.
  - Dashboard and Portfolio unavailable state copy is unified to `資料不足不顯示`, with info-tooltips that explain why values are intentionally hidden.
  - Portfolio holdings net-share visibility note is moved into a tooltip to keep the page clean while preserving discoverability.
  - Import modal baseline labels/placeholders are normalized to `可空`, `期初持倉（可多筆）` is renamed to `期初持倉`, and preview balance-action wording/tooltips are made user-facing.
  - Import preview usability is improved by narrowing the `賣先買後處理` column and widening the modal to reduce horizontal scrolling.
- Reproducible 2025 MD data points used in this round:
  - `startValueSource=0`
  - `endValueSource=105686.84`
  - `netContributionsSource=100000`
  - `coverageDays=2`
  - `hasOpeningBaseline=false`
  - `usesPartialHistoryAssumption=true`
  - `denominator≈274.7252747`
  - `numerator=5686.84`
  - `ModifiedDietz≈2070.01%`
  - `TimeWeightedReturn≈5.68684%`
- Why the Modified Dietz becomes extreme in this case (concrete readable explanation):
  - With `hasOpeningBaseline=false`, the year starts from zero baseline and the method must infer return from a partial-history setup.
  - The net contribution (`100000`) arrives very late in the year (`coverageDays=2`), so time-weighting shrinks the effective denominator to about `274.7252747`.
  - Absolute gain (`numerator=5686.84`) is not unusually large by itself, but dividing by a near-zero denominator amplifies the MD percentage.
  - This yields `ModifiedDietz≈2070.01%`, which is mathematically consistent with cash-flow timing rather than a normal full-year growth profile.
  - `TimeWeightedReturn≈5.68684%` remains comparatively moderate because TWR is less sensitive to the same late-flow denominator compression.

## Verification Evidence (Group I Execution Log)

### Commands Executed

```bash
dotnet test "/workspaces/InvestmentTracker/backend/tests/InvestmentTracker.Application.Tests/InvestmentTracker.Application.Tests.csproj" --filter "FullyQualifiedName~HistoricalPerformanceServiceReturnTests|FullyQualifiedName~CalculateAggregateYearPerformanceUseCaseTests"

dotnet test "/workspaces/InvestmentTracker/backend/tests/InvestmentTracker.API.Tests/InvestmentTracker.API.Tests.csproj" --filter "FullyQualifiedName~StockTransactionsImportControllerTests"

npm --prefix "/workspaces/InvestmentTracker/frontend" run test:run -- src/test/performance.metrics-binding.test.tsx src/test/dashboard.aggregate-fixed.test.tsx src/test/portfolio.performance-metrics.test.tsx src/test/portfolio.page.non-transaction-cache.test.tsx src/test/stock-import.balance-action.test.tsx

npm --prefix "/workspaces/InvestmentTracker/frontend" run type-check
```

### Command Outcome Summary

| Command Scope | Result | Outcome |
|---|---|---|
| Backend application regression (`HistoricalPerformanceServiceReturnTests` + `CalculateAggregateYearPerformanceUseCaseTests`) | PASS | Failed: 0, Passed: 18, Total: 18 |
| Backend API regression (`StockTransactionsImportControllerTests`) | PASS | Failed: 0, Passed: 23, Total: 23 |
| Frontend combined regression (`performance.metrics-binding` + `dashboard.aggregate-fixed` + `portfolio.performance-metrics` + `portfolio.page.non-transaction-cache` + `stock-import.balance-action`) | PASS | 5 test files passed, 48 tests passed, 0 failed |
| Frontend type-check | PASS | Type-check completed without errors |

### Group I Traceability (T098-T110)

| Task(s) | Key File(s) | Key Test(s) / Evidence |
|---|---|---|
| T098, T099 | `frontend/src/pages/Performance.tsx` | `frontend/src/test/performance.metrics-binding.test.tsx` (annual copy and low-confidence wording assertions) |
| T100 | `frontend/src/pages/Dashboard.tsx` | `frontend/src/test/dashboard.aggregate-fixed.test.tsx` (`資料不足不顯示` copy + tooltip reason assertions) |
| T101 | `frontend/src/components/portfolio/PerformanceMetrics.tsx` | `frontend/src/test/portfolio.performance-metrics.test.tsx` (`資料不足不顯示` copy + tooltip reason assertions) |
| T102 | `frontend/src/pages/Portfolio.tsx` | `frontend/src/test/portfolio.page.non-transaction-cache.test.tsx` (net-share visibility note rendered via tooltip) |
| T103, T104, T106 | `frontend/src/components/import/CSVImportModal.tsx` | `frontend/src/test/stock-import.balance-action.test.tsx` (baseline label/placeholder, heading rename, and preview usability assertions) |
| T105 | `frontend/src/components/import/StockImportButton.tsx`, `frontend/src/components/import/CSVImportModal.tsx` | `frontend/src/test/stock-import.balance-action.test.tsx` (user-friendly balance-action wording + tooltip guidance) |
| T107 | `frontend/src/test/performance.metrics-binding.test.tsx`, `frontend/src/test/dashboard.aggregate-fixed.test.tsx`, `frontend/src/test/portfolio.performance-metrics.test.tsx`, `frontend/src/test/portfolio.page.non-transaction-cache.test.tsx`, `frontend/src/test/stock-import.balance-action.test.tsx` | Combined frontend regression closure evidence in this round (performance/dashboard/portfolio/import balance: 48 passed) |
| T108 | `backend/tests/InvestmentTracker.Application.Tests/HistoricalPerformanceServiceReturnTests.cs`, `backend/tests/InvestmentTracker.Application.Tests/UseCases/CalculateAggregateYearPerformanceUseCaseTests.cs`, `backend/tests/InvestmentTracker.API.Tests/Controllers/StockTransactionsImportControllerTests.cs` | Reproducible 2025 MD assertions include `startValueSource=0`, `endValueSource=105686.84`, `netContributionsSource=100000`, `coverageDays=2`, `hasOpeningBaseline=false`, `usesPartialHistoryAssumption=true`, `denominator≈274.7252747`, `numerator=5686.84`, `ModifiedDietz≈2070.01%`, and `TimeWeightedReturn≈5.68684%` |
| T109 | `specs/012-import-broker-statement/quickstart.md` | This Group I notes/evidence/traceability update with concrete MD readability explanation |
| T110 | `specs/012-import-broker-statement/tasks.md` | Group I checklist completion recorded |
