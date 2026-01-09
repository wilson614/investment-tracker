# InvestmentTracker Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-01-06

## Active Technologies
- SQLite (development), PostgreSQL (production-compatible) (001-portfolio-tracker)
- C# .NET 8 (Backend), TypeScript 5.x with React (Frontend) + ASP.NET Core 8, Entity Framework Core, React 18, Vite, TanStack Query (001-portfolio-tracker)

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
- 001-portfolio-tracker: Added C# .NET 8 (Backend), TypeScript 5.x with React (Frontend) + ASP.NET Core 8, Entity Framework Core, React 18, Vite, TanStack Query
- 001-portfolio-tracker: Added C# .NET 8 (Backend), TypeScript 5.x (Frontend)

- 001-portfolio-tracker: Added C# .NET 8 (Backend), TypeScript 5.x (Frontend)

<!-- MANUAL ADDITIONS START -->

## Git Commit Preferences

- **不要**在 commit message 結尾加上 `🤖 Generated with [Claude Code]` 和 `Co-Authored-By: Claude` 這幾行
- Commit message 使用簡潔的 conventional commits 格式

## Development Rules

- **修正功能前先檢查 spec.md**：任何功能修改前，必須先檢視 `specs/001-portfolio-tracker/spec.md` 確認是否需要同步更新規格
- **規格同步**：如果修正涉及需求變更，先更新 spec.md 再實作

<!-- MANUAL ADDITIONS END -->
