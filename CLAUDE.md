# InvestmentTracker Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-01-06

## Active Technologies
- SQLite (development), PostgreSQL (production-compatible) (001-portfolio-tracker)
- C# .NET 8 (Backend), TypeScript 5.x with React (Frontend) + ASP.NET Core 8, Entity Framework Core, React 18, Vite, TanStack Query (001-portfolio-tracker)
- C# .NET 8 (Backend), TypeScript 5.x (Frontend) + ASP.NET Core 8, Entity Framework Core, React 18, Vite, TanStack Query (001-portfolio-tracker)

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
- 001-portfolio-tracker: Added C# .NET 8 (Backend), TypeScript 5.x (Frontend) + ASP.NET Core 8, Entity Framework Core, React 18, Vite, TanStack Query
- 001-portfolio-tracker: Added C# .NET 8 (Backend), TypeScript 5.x (Frontend) + ASP.NET Core 8, Entity Framework Core, React 18, Vite, TanStack Query
- 001-portfolio-tracker: Added C# .NET 8 (Backend), TypeScript 5.x with React (Frontend) + ASP.NET Core 8, Entity Framework Core, React 18, Vite, TanStack Query


<!-- MANUAL ADDITIONS START -->

## Git Commit Preferences

- **不要**在 commit message 結尾加上 `🤖 Generated with [Claude Code]` 和 `Co-Authored-By: Claude` 這幾行
- Commit message 使用簡潔的 conventional commits 格式

## Development Rules

### Spec-Driven Development (SDD) 工作流程

**⚠️ 強制性 Checklist** - 任何涉及功能/架構變更的請求，必須依照以下順序執行：

1. **第一步永遠是讀取 spec.md**：`specs/001-portfolio-tracker/spec.md`
2. **確認變更範圍**：這個變更是否需要更新規格？
3. **先更新規格**：如果需求涉及變更，**必須先**更新 spec.md 再開始實作
4. **規劃任務**：使用 TodoWrite 規劃實作步驟
5. **開始實作**：依照規劃執行程式碼修改
6. **Commit 前檢查**：再次確認 spec.md 是否完整反映變更

**觸發條件**（以下情況必須執行 SDD 流程）：
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

<!-- MANUAL ADDITIONS END -->
