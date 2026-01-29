# InvestmentTracker Development Guidelines

## Project Overview

- **Tech Stack**: C# .NET 8 (Backend), TypeScript 5.x + React 18 (Frontend)
- **Database**: PostgreSQL (development and production)
- **Framework**: ASP.NET Core 8, Entity Framework Core, Vite, TanStack Query

## Project Structure

```text
backend/
frontend/
tests/
specs/          # Speckit specification files
.specify/       # Speckit configuration
```

## Local Development

- Frontend dev server: port `3000`
- Backend dev server: port `5000`
- If ports occupied, allowed to stop occupying processes

## Git Commit Preferences

- **No** `🤖 Generated with [Claude Code]` or `Co-Authored-By: Claude` suffix
- Use concise conventional commits format
- Follow `git-keeper` skill rules

## Speckit Integration (Standard Mode)

This project uses Speckit. Specification files location:
- `specs/<module-name>/spec.md` - Feature specification
- `specs/<module-name>/plan.md` - Implementation plan
- `specs/<module-name>/tasks.md` - Task checklist

**Workflow:**
```
/speckit.specify → /speckit.clarify → /speckit.plan → /speckit.tasks
    ↓
/team-exec (PM reads tasks.md, executes with team)
```

**Trigger Conditions** (must use Speckit flow):
- Route structure changes
- Data model changes
- Add/remove features
- Architecture refactoring
- UI/UX flow changes
- Cache strategy changes

## Error Handling

Use Domain Exceptions (no try-catch in Controllers):

| Exception | HTTP Status |
|:---|:---|
| `EntityNotFoundException` | 404 Not Found |
| `AccessDeniedException` | 403 Forbidden |
| `BusinessRuleException` | 400 Bad Request |

Controllers should NOT contain try-catch. Use Cases throw Domain Exceptions.

## Build & Test

QA engineer handles build verification. Do not manually kill processes during team execution.

**On conversation end** (when user takes over for testing):
```bash
taskkill /F /IM node.exe    # Stop frontend
taskkill /F /IM dotnet.exe  # Stop backend
```

This releases ports 3000/5000 for manual testing.
