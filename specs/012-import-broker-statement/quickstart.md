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

1. Use a broker file with malformed numeric values.
2. Verify row-level diagnostics include `rowNumber`, `fieldName`, `errorCode`, and correction guidance.
3. Simulate sync failure for unresolved names.
4. Verify resolvable rows can still proceed while unresolved rows require manual ticker input.
5. Attempt execute with unresolved shortfall decision.
6. Verify affected rows are blocked with explicit failure reason.

## Expected Verification Artifacts
- API response payloads for preview and execute.
- UI screenshots for:
  - format detection/override
  - unresolved symbol remediation
  - insufficient-balance decision UI
  - final summary with row-level outcomes
- Backend logs for on-demand sync attempts and import result aggregation.
