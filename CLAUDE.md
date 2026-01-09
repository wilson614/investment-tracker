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

當用戶同意執行需求修改後，**必須**依照以下順序執行：

1. **檢查 spec.md**：先檢視 `specs/001-portfolio-tracker/spec.md` 確認是否需要同步更新規格
2. **更新規格**：如果需求涉及變更，先更新 spec.md
3. **規劃任務**：使用 TodoWrite 規劃實作步驟
4. **開始實作**：依照規劃執行程式碼修改
5. **完成後檢查**：commit 前再次確認 spec.md 是否需要補充

### 其他規則

- **規格優先**：spec.md 是需求的唯一真實來源 (Single Source of Truth)
- **同步更新**：任何影響功能行為的修改都必須反映在 spec.md

<!-- MANUAL ADDITIONS END -->
