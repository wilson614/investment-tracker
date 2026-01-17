# Implementation Plan: Portfolio Enhancements V2

**Branch**: `002-portfolio-enhancements` | **Date**: 2026-01-18 | **Spec**: `specs/002-portfolio-enhancements/spec.md`
**Input**: Feature specification from `specs/002-portfolio-enhancements/spec.md`
**Base Module**: Extends `001-portfolio-tracker`

## Summary

本次是針對既有 `002-portfolio-enhancements` 的 **plan.md 檢查並更新**（不是重建）。

新增/更新重點（以 `spec.md` 的最新 Clarifications 與 FR 為準）：

- **Benchmark negative caching（P1/可靠性）**：避免未上市 benchmark（例：2021 的 HCHA/EXUS）每次重整都重打 Stooq。
- **Current-year comparison render gate（UX）**：現年度比較要等 holdings realtime prices + benchmarks realtime prices 都 ready 才 render，避免初次 0 / flicker。
- **Benchmark selection cap（UX/保護外部 API）**：現年度比較 benchmark multi-select 上限 **10**。
- **Single portfolio + FX auto-fill（策略）**：避免多投組；交易可省略 ExchangeRate，但在 TWD-based metrics 需用交易日 historical FX auto-fill（失敗則提示手動）。

## Technical Context

**Language/Version**: C# .NET 8 (Backend), TypeScript 5.x (Frontend)
**Primary Dependencies**: ASP.NET Core 8, Entity Framework Core, React 18, Vite, TanStack Query, Recharts
**Storage**: PostgreSQL (primary), SQLite (development fallback)
**Testing**: xUnit (backend), Jest/React Testing Library (frontend)
**Target Platform**: Docker containers, self-hosted NAS/VPS
**Project Type**: Web application (frontend + backend)

## Constitution Check

*GATE: All principles verified ✓*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Clean Architecture | ✓ Pass | 變更依 layer 放置（API/Application/Infrastructure） |
| II. Multi-Tenancy | ✓ Pass | Benchmark / historical caches 為 global；使用者資料仍需隔離 |
| III. Accuracy First | ✓ Pass | FX auto-fill 使用交易日 historical FX，並保留原幣資訊 |
| IV. Self-Hosted Friendly | ✓ Pass | DB cache 降低外部 API 依賴與 rate limit 風險 |
| V. Technology Stack | ✓ Pass | 不引入新框架 |

## Project Structure

```text
backend/
└── src/
frontend/
└── src/

specs/002-portfolio-enhancements/
├── spec.md
├── plan.md
└── tasks.md
```

## Key Design Decisions (Updated)

### 1) Benchmark negative caching (highest priority)

- Scope: `GET /api/market-data/benchmark-returns?year=...`
- Problem: Stooq 回傳 null（例如 ETF 尚未上市）導致每次 refresh 都再次嘗試 fetch。
- Decision: 對 `(MarketKey, YearMonth)` 持久化 NotAvailable marker（no TTL），後續直接 return null，不再 call Stooq（除非 DB 手動清除該 marker）。
- Storage: 以現有 benchmark snapshot table 為基礎延伸（避免再新增一張用途重疊的表）。

### 2) Current-year comparison render gate

- Decision: UI 以 “ready gate” 控制 render：holdings realtime prices + selected benchmarks realtime prices 全部到齊才 render；切換 benchmark 時保持上一個數值直到新資料 ready（避免 flicker）。

### 3) Benchmark selection cap (10)

- Decision: 現年度比較允許 multi-select，但最多 10 個（built-in + custom 合併計算）。

### 4) Single portfolio + FX auto-fill

- Decision: 不走多投組貨幣模式；`StockTransaction.ExchangeRate` 可為 null。
- Rule: 需要產出 TWD-based metrics 時，若交易沒有 ExchangeRate，使用交易日 historical FX auto-fill；查不到時提示手動補。

## Notes

- 本文件只描述本次新增/修正方向；其餘既有 story（US1-US8）維持既有設計與已完成內容。
