# Feature Specification: Unified Broker Statement Import

**Feature Branch**: `[012-import-broker-statement]`
**Created**: 2026-02-15
**Status**: Draft
**Input**: User description: "新增匯入功能：同一個CSV匯入入口要能支援台股證券app匯出的對帳單（範例檔在專案根目錄）。匯入後可直接建立投組交易，並讓使用者選擇資金來源行為（補差額或融資），行為需與現有機制一致。請同時確認目前既有CSV匯入在餘額不足時是否會詢問（需比照手動新增交易流程）。可評估自動判斷證券app格式 vs 原CSV格式。開始Speckit流程。"

## Clarifications

### Session 2026-02-15

- Q: When broker statement rows do not include explicit buy/sell, how should trade side be determined? → A: Derive from net settlement sign first (negative=buy, positive=sell); if ambiguous, require per-row manual confirmation before execution.
- Q: Should security identity resolution use online lookup during import or local mapping? → A: Use local database mapping as the primary source.
- Q: How should security resolution behave when a statement row cannot be matched locally? → A: Attempt an on-demand synchronization from TWSE ISIN source when local mapping misses; if still unresolved, require user manual verification/correction before import execution.
- Q: If on-demand synchronization fails, should import stop or continue? → A: Continue importing rows that are fully resolvable; unresolved rows must be listed in order with their parsed security names and require user ticker input before those rows can execute.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Import broker statement from existing entry (Priority: P1)

As an investor, I can use the same existing import entry to upload a Taiwan broker statement CSV and preview normalized trades before creating portfolio transactions.

**Why this priority**: This is the primary business value: reducing manual entry by importing broker-exported statements directly.

**Independent Test**: Can be fully tested by uploading a valid broker statement CSV from the existing import entry and confirming a correct trade preview is generated.

**Acceptance Scenarios**:

1. **Given** the user opens the existing transaction import entry, **When** the user uploads a broker statement CSV with recognizable headers, **Then** the system identifies it as broker-statement format and shows a normalized preview of importable trades.
2. **Given** the system cannot confidently determine the format, **When** the user selects the intended format manually, **Then** the preview refreshes with that format and the user can continue the import flow.
3. **Given** broker-statement rows do not include explicit buy/sell values, **When** trade side is inferred from net settlement sign and any row remains ambiguous, **Then** the system requires per-row manual confirmation before execution.

---

### User Story 2 - Resolve insufficient balance during import (Priority: P1)

As an investor, when imported buy trades exceed available cash balance, I must be prompted to choose the same balance-handling behavior used in manual transaction creation.

**Why this priority**: The user explicitly requires parity with manual transaction behavior; without this, imports fail or behave inconsistently.

**Independent Test**: Can be fully tested by importing at least one buy row that exceeds available balance and verifying the user must choose Margin or Top-up before that trade is created.

**Acceptance Scenarios**:

1. **Given** an import contains buy rows with insufficient available balance, **When** the user proceeds with import, **Then** the system requires a balance-handling choice (Margin or Top-up) before creating affected trades.
2. **Given** the user selects Top-up for affected rows, **When** the user chooses a valid top-up transaction type and confirms, **Then** the system creates trades using that top-up behavior and reports the result.
3. **Given** the user does not complete the balance-handling choice, **When** import execution is attempted, **Then** affected rows are not created and a clear row-level reason is shown.

---

### User Story 3 - Preserve existing CSV import behavior (Priority: P2)

As an existing user, I can continue importing the original CSV format from the same entry without regression.

**Why this priority**: Backward compatibility is required to avoid breaking current workflows while adding broker-statement support.

**Independent Test**: Can be fully tested by importing a valid legacy CSV file and confirming the file is accepted, previewed, and imported with unchanged expected outcomes.

**Acceptance Scenarios**:

1. **Given** the user uploads a valid legacy CSV file, **When** the user completes import, **Then** transactions are created successfully with no required migration to a new file format.
2. **Given** an import file contains invalid rows, **When** import finishes, **Then** the system provides row-level error messages while preserving successful rows.

---

### Edge Cases

- What happens when a CSV includes headers that partially match both supported formats?
- How does the system handle broker-statement rows where trade side cannot be derived reliably from provided values?
- How does the system handle rows that identify a security name but not a unique tradable symbol?
- How does the system handle unresolved rows when synchronization fails and users must enter tickers manually in sequence?
- What happens when one import contains multiple insufficient-balance buy rows requiring different user decisions?
- How does the system handle localized number formats (thousand separators, negative signs) that are malformed in some rows?
- What happens when a user attempts to import the same file twice in the same session?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST support importing both legacy transaction CSV files and Taiwan broker-statement CSV files through the same existing import entry.
- **FR-002**: The system MUST detect the most likely file format after upload and present the detected format to the user before execution.
- **FR-003**: The system MUST allow the user to manually override the detected format before import execution.
- **FR-004**: For broker-statement format, the system MUST parse and normalize trade date, security identity, quantity, unit price, gross trade amount, fees/taxes, currency, and net settlement amount from the file.
- **FR-005**: The system MUST derive trade direction (buy/sell) from net settlement sign using the canonical rule (negative=buy, positive=sell) and display the derived result in preview.
- **FR-005a**: For rows where trade direction remains ambiguous after applying the canonical rule, the system MUST require per-row manual confirmation before execution.
- **FR-006**: The system MUST resolve broker-statement security identity using a local security mapping dataset as the primary source.
- **FR-006a**: When local mapping does not resolve a row, the system MUST attempt an on-demand synchronization from the TWSE ISIN source before final resolution.
- **FR-006b**: If a row remains unresolved or non-unique after on-demand synchronization, the system MUST require user manual verification/correction or exclusion before execution.
- **FR-006c**: If on-demand synchronization fails, the system MUST continue processing rows that are already fully resolvable.
- **FR-006d**: Unresolved rows MUST be displayed in original file order with the parsed security name and MUST require user ticker input before those rows can execute.
- **FR-007**: The system MUST provide an import preview before creating transactions.
- **FR-008**: For every imported buy row that would cause insufficient available balance, the system MUST require an explicit balance-handling choice before creating that row.
- **FR-009**: Balance-handling options for insufficient rows MUST include Margin and Top-up, and Top-up MUST require choosing a top-up transaction type equivalent to manual transaction creation behavior.
- **FR-010**: The system MUST allow the user to apply one balance-handling choice to all insufficient rows in the current import session, while still allowing per-row adjustment before final confirmation.
- **FR-011**: If balance-handling remains unresolved for any row, the system MUST not create that row and MUST provide a clear row-level failure reason.
- **FR-012**: Legacy CSV import capability and expected outcomes MUST remain functionally equivalent to current behavior.
- **FR-013**: The system MUST provide an import result summary including total rows, successful rows, failed rows, and row-level failure reasons.
- **FR-014**: Successfully imported rows MUST create portfolio transactions whose displayed values match the final preview values.

### Key Entities *(include if feature involves data)*

- **Import Session**: A user-initiated import run containing uploaded file metadata, selected/detected format, preview rows, and final execution summary.
- **Import Row**: A normalized transaction candidate derived from one input row, including parsed fields, validation status, and import outcome.
- **Balance Handling Decision**: User-selected action for insufficient-balance buy rows (Margin or Top-up), including optional top-up transaction type.
- **Broker Statement Record**: Raw row from broker-exported statement containing broker-specific columns and localized numeric/date formats.
- **Legacy CSV Record**: Raw row from the existing supported import format with previously accepted column semantics.
- **Security Mapping Dataset**: A locally stored mapping between exchange security names and tradable identifiers used during import-time resolution.

## Dependencies

- Existing manual transaction balance-handling policy and options (Margin / Top-up and valid top-up types) remain the normative behavior source.
- Broker-statement sample and additional statement files used for acceptance testing are available to product and QA teams.
- TWSE ISIN source can be reached for on-demand synchronization attempts.
- Local security mapping dataset is available during import validation.

## Assumptions

- The initial scope covers equity trade rows relevant to portfolio transaction import.
- Broker statement files may not contain an explicit buy/sell column; trade direction is inferred from provided settlement-related values.
- Existing manual transaction behavior is the source of truth for insufficient-balance decision types and validation rules.
- If a row cannot be normalized into a valid transaction candidate, it is excluded from creation and reported as a row-level failure.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The provided broker-statement sample file in the repository reaches a valid import preview in 100% of acceptance test runs without manual column mapping.
- **SC-002**: 100% of imported buy rows that would exceed available balance require an explicit user decision (Margin or Top-up) before creation.
- **SC-003**: 100% of successfully imported rows appear in the target portfolio transaction list with date, quantity, price, and charges matching the approved preview.
- **SC-004**: Legacy CSV import acceptance scenarios continue passing with no regression in expected user outcomes.
- **SC-005**: For supported broker-statement files, at least 95% of valid rows are imported successfully in acceptance testing, and invalid rows always include actionable row-level error reasons.
- **SC-006**: For unresolved local mappings, 100% of rows trigger an on-demand synchronization attempt before being marked unresolved.
- **SC-007**: Rows still unresolved after synchronization are always surfaced with actionable manual-verification guidance before execution.
- **SC-008**: 100% of unresolved rows are presented in original file order with parsed security names and accept manual ticker input prior to execution.
