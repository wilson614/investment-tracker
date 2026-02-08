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

### Main Agent Prohibited Operations

The following operations **MUST NOT** be executed directly by Main Agent. Always delegate to the appropriate SubAgent:

| Operation | Prohibited | Correct |
|:---|:---|:---|
| Build verification | `Bash(dotnet build)` | `Task(subagent_type="qa-engineer")` |
| Test execution | `Bash(dotnet test)` | `Task(subagent_type="qa-engineer")` |
| Type checking | `Bash(npm run type-check)` | `Task(subagent_type="qa-engineer")` |
| E2E testing | `Bash(npx playwright test)` | `Task(subagent_type="qa-engineer")` |

**Even for "simple commands", delegation is mandatory.** Main Agent's role is coordination, not execution.

### Quality Gate Verification

**The formal QA verification step in the workflow MUST be performed by `qa-engineer`, not by the implementing engineer.**

| Phase | Who Can Run Tests |
|:---|:---|
| During implementation | Engineer may run tests for own development |
| Quality Gate (formal verification) | **MUST be `qa-engineer`** - no exceptions |

**Wrong workflow:**
```
backend-engineer implements → backend-engineer verifies → code-reviewer
```

**Correct workflow:**
```
backend-engineer implements → qa-engineer verifies → code-reviewer
```

Engineers verifying their own work in the formal QA step defeats the purpose of independent verification.

**On conversation end** (when user takes over for testing):
```bash
taskkill /F /IM node.exe    # Stop frontend
taskkill /F /IM dotnet.exe  # Stop backend
```

This releases ports 3000/5000 for manual testing.
