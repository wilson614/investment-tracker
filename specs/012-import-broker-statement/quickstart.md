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
