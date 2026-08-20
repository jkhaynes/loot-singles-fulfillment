# Implementation Plan: TCGplayer Packing Slip Order Import

**Branch**: `001-tcgplayer-order-import` | **Date**: 2026-08-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-tcgplayer-order-import/spec.md`

## Summary

Parse a TCGplayer Packing Slip PDF (which commonly bundles many orders in one file, each on its own page, followed by a summary/index page) into validated `Order` + `OrderLine` records, evaluating and persisting each order independently so one bad order never blocks the others. Parsing is defensive per-field (product name, set, collector number, condition, and quantity are required; rarity and variant/printing are optional but variant/printing must be preserved whenever present, including when combined with condition, e.g. "Near Mint Holofoil"). Persistence of each order is atomic, duplicate order identifiers are rejected with a database-enforced uniqueness guarantee, and the source PDF (which carries customer shipping PII on every page) is never retained beyond the single synchronous import operation. This is a backend-only application-service capability — no picker UI, no order claiming, no catalog/image enrichment — exposing progress and results for a future caller (an import UI feature) to consume.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (LTS) — per approved stack (PRD §40.3, README)

**Primary Dependencies**: ASP.NET Core (hosting only, no HTTP contract added by this feature — see Project Structure), Entity Framework Core (persistence), PdfPig (PDF text extraction — see research.md for selection rationale)

**Storage**: Azure SQL Database in production (PRD §40.4/§40.8); SQL Server (LocalDB or containerized) for local development and integration tests, via EF Core so the provider is swappable

**Testing**: xUnit (backend unit/integration tests, per constitution Principle IV and PRD §40.9)

**Target Platform**: ASP.NET Core Web API service, hosted on Azure Container Apps (PRD §40.8); this feature adds no new HTTP endpoints, only an internal application service

**Project Type**: Web application (backend + future frontend, PRD §40.1/§40.2) — this feature touches only the backend; no frontend changes, matching the spec's explicit exclusion of picker/import UI

**Performance Goals**: Synchronous processing of a full batch (up to ~200 orders observed in real usage, spec SC-006) within a single caller-awaited call, with incremental progress observable throughout — no fixed latency target beyond "synchronous and practical at this scale" (spec Assumptions)

**Constraints**: Per-order atomicity (FR-016); database-enforced uniqueness on TCGplayer order identifier (FR-008); zero retention of the source PDF or any copy of it, in any outcome (FR-019); no card catalog/image enrichment (FR-011); no HTTP/UI surface for supplying the PDF (deferred, spec Assumptions)

**Scale/Scope**: Single-business internal tool; up to ~200 orders per import batch; V1 target games (Pokémon, Magic, Lorcana, One Piece) all share the same packing-slip line format observed in real data

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status |
|---|---|---|
| I. Product Owner Authority | Plan implements the approved, PO-clarified spec only; no PO decision overridden | PASS |
| II. No Invented Requirements | Every design element below traces to a spec FR/SC; no picker UI, claiming, or catalog enrichment added | PASS |
| III. Small, Reviewable Changes | Work happens on feature branch `001-tcgplayer-order-import`, not `main` | PASS |
| IV. Test-Driven Development | Quantity preservation (FR-003), safe/defensive parsing (FR-005–FR-007, FR-015, FR-018), and atomic persistence (FR-016) are all new application behavior requiring Red → Green → Refactor coverage; enforced during `/speckit-implement` execution per constitution Principle IV, not by this plan alone | PASS (design supports it; execution-time gate) |
| V. Safe Failure Over Silent Corruption | This entire feature *is* that principle: defensive rejection (FR-005–FR-007, FR-015), no silent partial orders (FR-016) | PASS |
| VI. Server-Enforced Critical Business Rules | Duplicate-order uniqueness enforced at the database layer (FR-008), not just in application code | PASS |
| VII. Data Minimization | FR-009/FR-019 drive the design: no customer PII field is ever mapped to a persisted column, and the source file is never written to durable storage | PASS |
| VIII. One Responsive Product | N/A — this feature has no UI at all (out of scope per spec) | N/A |
| IX. Replaceable Integrations | PDF parsing is isolated behind an `IPackingSlipParser` abstraction (Application layer), implemented by an Infrastructure adapter around PdfPig — swappable for a future TCGplayer API without touching the import/persistence orchestration | PASS |
| X. Single-Business Scope | No multi-tenant concepts introduced | PASS |
| XI. Reliability During Fulfillment | Synchronous design (FR-014) with per-order atomicity avoids partial-batch corruption on failure; no cost/reliability tradeoff introduced | PASS |

No violations to justify — Complexity Tracking is empty.

**Post-Phase 1 re-check**: research.md and data-model.md confirm the `IPackingSlipParser` isolation (IX), the database-level unique constraint (VI), and zero PII/source-file persistence (VII) as concrete designs rather than intentions. No new violations were introduced by the detailed design; all gates remain PASS/N/A as above.

## Project Structure

### Documentation (this feature)

```text
specs/001-tcgplayer-order-import/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   └── import-service.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

This is the first feature with any implementation code. Per the approved technology stack (PRD §40.1/§40.2), the eventual system is a web application with a separately-deployable backend (ASP.NET Core Web API → Azure Container Apps) and frontend (React/TypeScript PWA → Azure Static Web Apps). This feature is backend-only and has no UI, so only `backend/` is created now; `frontend/` is added by whichever future feature first needs it (YAGNI — no frontend scaffolding is invented here).

```text
backend/
├── src/
│   ├── LootSingles.Domain/
│   │   └── Orders/                       # Order, OrderLine entities (FR-001–FR-004, FR-016–FR-018)
│   ├── LootSingles.Application/
│   │   └── Import/
│   │       ├── IPackingSlipParser.cs     # Parsing abstraction (Constitution IX — replaceable)
│   │       ├── IPackingSlipImportService.cs  # Public contract, see contracts/import-service.md
│   │       ├── PackingSlipImportService.cs   # Orchestrates parse → validate → persist per order
│   │       ├── ImportAttempt.cs          # Import Attempt entity (FR-012–FR-015, FR-019)
│   │       ├── ImportOrderResult.cs      # Import Order Result entity (FR-005, FR-006, FR-012)
│   │       └── FailureType.cs            # Stable failure-type codes (FR-006)
│   ├── LootSingles.Infrastructure/
│   │   ├── Import/
│   │   │   └── PdfPigPackingSlipParser.cs  # IPackingSlipParser implementation (research.md)
│   │   └── Persistence/
│   │       ├── LootSinglesDbContext.cs
│   │       └── Configurations/           # EF Core entity configs, incl. unique index (FR-008)
│   └── LootSingles.Api/
│       └── Program.cs                    # Host only — no new endpoints added by this feature
├── tests/
│   ├── LootSingles.UnitTests/
│   │   └── Import/                       # Parser + validation rules (FR-002, FR-005, FR-018)
│   ├── LootSingles.IntegrationTests/
│   │   └── Import/                       # Real fixture PDFs → persisted Order/OrderLine (FR-001–FR-019)
│   └── LootSingles.Fixtures/
│       └── PackingSlips/                 # Sanitized representative packing-slip PDFs
```

**Structure Decision**: Layered backend structure (Domain → Application → Infrastructure → Api), consistent with the constitution's requirement that the backend own critical business rules (Principle VI) and that TCGplayer import be an isolated, replaceable adapter (Principle IX). `IPackingSlipParser` is the seam a future TCGplayer API integration replaces; `IPackingSlipImportService` is the seam a future import-UI feature calls through (spec FR-014's "caller"). No `LootSingles.Api` HTTP endpoint is added — the spec explicitly defers "how an operator supplies the PDF" to a future feature (spec Assumptions), so this feature stops at the application-service boundary.
