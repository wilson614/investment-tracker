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
| Performance response | `xirr*`, `totalReturn*`, `modifiedDietz*`, `timeWeightedReturn*`, `startValue*`, `endValue*`, `earliestTransactionDateInYear` | NULL allowed (especially when `isComplete=false`) |
| Performance response | `missingPrices` | Must be empty when `isComplete=true`; may be non-empty when `isComplete=false` |

### Failure Criteria (any one = FAIL)

- Any required endpoint returns non-2xx (except explicitly expected validation failures during negative checks).
- Preview fixed assertions fail (`summary.totalRows != 50`, `rows.length != 50`, or summary arithmetic mismatch).
- Execute arithmetic mismatch (`totalRows != insertedRows + failedRows`, `totalRows != results.length`, or `errorCount != errors.length`).
- Row-level contract violation (`success=true` with null `transactionId`, or `success=false` with null `errorCode`).
- `performance/years` does not include `2025` and `2026` after successful import.
- `performance/year` for `2026` returns `transactionCount == 0` after successful import.
- UI/API binding mismatch for performance cards (`資金加權報酬率`, `時間加權報酬率`, `年度摘要`) versus response field mapping above.


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
