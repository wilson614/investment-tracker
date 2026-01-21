# InvestmentTracker Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-01-06

## Active Technologies
- SQLite (development), PostgreSQL (production-compatible) (001-portfolio-tracker)
- C# .NET 8 (Backend), TypeScript 5.x with React (Frontend) + ASP.NET Core 8, Entity Framework Core, React 18, Vite, TanStack Query (001-portfolio-tracker)
- C# .NET 8 (Backend), TypeScript 5.x (Frontend) + ASP.NET Core 8, Entity Framework Core, React 18, Vite, TanStack Query (001-portfolio-tracker)
- C# .NET 8 (Backend), TypeScript 5.x (Frontend) + ASP.NET Core 8, Entity Framework Core, React 18, Vite, TanStack Query, Recharts (002-portfolio-enhancements)
- PostgreSQL (primary), SQLite (development) (002-portfolio-enhancements)
- PostgreSQL (primary), SQLite (development fallback) (002-portfolio-enhancements)

- C# .NET 8 (Backend), TypeScript 5.x (Frontend) (001-portfolio-tracker)

## Project Structure

```text
backend/
frontend/
tests/
```

## Commands

npm test; npm run lint

## Code Style

C# .NET 8 (Backend), TypeScript 5.x (Frontend): Follow standard conventions

## Recent Changes
- 002-portfolio-enhancements: Added C# .NET 8 (Backend), TypeScript 5.x (Frontend) + ASP.NET Core 8, Entity Framework Core, React 18, Vite, TanStack Query, Recharts
- 002-portfolio-enhancements: Added C# .NET 8 (Backend), TypeScript 5.x (Frontend) + ASP.NET Core 8, Entity Framework Core, React 18, Vite, TanStack Query, Recharts


<!-- MANUAL ADDITIONS START -->

## 本機偵錯環境備註

- Frontend dev server 使用 `3000` port
- Backend dev server 使用 `5000` port
  - 若 `3000` / `5000` 被佔用，允許直接停止佔用程序（這兩個 port 保留給本機偵錯用）
- 本機偵錯 DB 使用 **PostgreSQL**
  - 請勿再使用 SQLite，避免與 production 行為不一致

## Git Commit Preferences

- **不要**在 commit message 結尾加上 `🤖 Generated with [Claude Code]` 和 `Co-Authored-By: Claude` 這幾行
- Commit message 使用簡潔的 conventional commits 格式

## Development Rules

### Spec-Driven Development (SDD) 工作流程 - 使用 Speckit

**⚠️ 強制性流程** - 任何涉及功能/架構變更的請求，**必須**依照以下 Speckit 標準流程執行：

#### 檔案位置
- 規格文件存放於 `specs/<module-name>/` 目錄
  - 功能規格：`specs/<module-name>/spec.md`
  - 實作計畫：`specs/<module-name>/plan.md`
  - 任務清單：`specs/<module-name>/tasks.md`
- Speckit 配置/模板存放於 `.speckit/` 目錄
- 目前主要模組：`specs/001-portfolio-tracker/`

#### 標準流程（必須依序執行）

1. **確保在正確的 Git Branch**
   - 新功能必須在對應的 feature branch 上開發
   - 執行 `git checkout -b feature/<feature-name>` 建立新分支

2. **更新規格**
   - **全新模組**（如 002-xxx）→ 執行 `/speckit.specify` 產生新的規格文件
   - **現有模組增量更新** → **直接手動編輯** `specs/<module-name>/spec.md`，不需執行 `/speckit.specify`

3. **釐清需求** → 執行 `/speckit.clarify`
   - 識別規格中不明確的部分，提出最多 5 個針對性問題
   - 將答案編碼回 spec.md

4. **制定計畫** → 執行 `/speckit.plan`
   - **全新模組** → 產生新的 `specs/<module-name>/plan.md`
   - **現有模組增量更新** → 在提示詞中**明確要求「檢查並更新」**（不可重建/覆蓋既有 plan），並聚焦於本次變更相關的章節（例如：新增的 story / cache 策略 / UX 規則）
   - **⚠️ 注意（覆蓋風險）**：`.specify/scripts/powershell/setup-plan.ps1` 目前會用 `Copy-Item ... -Force` 覆蓋 `plan.md`；除非你就是要重建，否則不要用它來處理既有模組

5. **產生任務** → 執行 `/speckit.tasks`
   - **全新模組** → 產生新的 `specs/<module-name>/tasks.md`
   - **現有模組增量更新** → 在提示詞中**明確要求「檢查並更新」**（不可重建/覆蓋既有 tasks），並確保任務以 user story 分 phase、遵守 tasks-template 的 checklist 格式

6. **品質分析** → 執行 `/speckit.analyze`
   - 跨文件一致性與品質檢查（spec.md、plan.md、tasks.md）

7. **執行實作** → 執行 `/speckit.implement`
   - 依照 tasks.md 中定義的任務順序執行實作

#### 觸發條件（以下情況必須執行 SDD 流程）
- 路由結構變更
- 資料模型變更
- 新增/移除功能
- 架構重構
- UI/UX 流程變更
- Cache 策略變更

### 其他規則

- **規格優先**：spec.md 是需求的唯一真實來源 (Single Source of Truth)
- **同步更新**：任何影響功能行為的修改都必須反映在 spec.md
- **不要跳過**：即使是「簡單的重構」也要先確認 spec.md
- **不要隨意修改**：沒有經過 Speckit 流程確認，不得直接修改功能程式碼

## 錯誤處理規範

### Domain Exceptions
專案使用 Domain Exceptions 進行錯誤處理，避免在 Controllers 中使用 try-catch：

- `EntityNotFoundException` - 實體找不到時拋出
- `AccessDeniedException` - 存取被拒絕時拋出
- `BusinessRuleException` - 業務規則違反時拋出

### ExceptionHandlingMiddleware
所有例外由 `ExceptionHandlingMiddleware` 統一處理，自動轉換為對應的 HTTP 狀態碼：
- `EntityNotFoundException` → 404 Not Found
- `AccessDeniedException` → 403 Forbidden
- `BusinessRuleException` → 400 Bad Request

### 規則
- Controllers 不應包含 try-catch（除非處理外部 API 錯誤）
- Use Cases 應拋出 Domain Exceptions 而非回傳 null 或 bool

## 建置與測試規則

### 建置完成後必須停止進程

**⚠️ 強制規則** - 確認建置成功後：
1. **必須停止所有 dev server 進程**（frontend/backend）
2. **釋放 port 3000 和 5000**，讓使用者可以手動測試
3. 不要保持進程持續運行

```bash
# 建置成功後執行
taskkill /F /IM node.exe  # 停止 frontend
taskkill /F /IM dotnet.exe  # 停止 backend
```

<!-- MANUAL ADDITIONS END -->
