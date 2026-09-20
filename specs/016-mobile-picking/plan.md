# Implementation Plan: Mobile Picking Experience

**Branch**: `016-mobile-picking` | **Date**: 2026-09-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/016-mobile-picking/spec.md`

## Summary

Almost all of this feature is presentation. The order detail API already returns every field
the work needs, and the claim endpoint already exists — what is missing is the interface that
uses them.

`OrderLineDetail` already carries `ProductLine` (the game), `Set`, `Quantity` and
`PickOutcome`. Grouping (FR-001 – FR-006), progress (FR-020 – FR-022) and the unresolved-set
guard (FR-016 – FR-019) are therefore all derivable on the client from a payload it already
receives. `POST /api/orders/{orderId}/claim` was built in feature 013 and is fully
concurrency-safe; FR-024 only needs a button wired to it.

**One backend change is required.** The dashboard cannot currently tell whether the signed-in
employee already holds an order, so FR-026 ("offer to resume") has nothing to read. The plan
adds a single `activeClaim` field to the dashboard read model.

The approach is deliberately additive: a set-grouping helper and a focused-view component
beside the existing list, sharing the same pick and report-issue calls. Nothing in the domain,
application or persistence layers changes except the one dashboard projection.

## Technical Context

**Language/Version**: C# / .NET 10 (backend); TypeScript 5 / React 18 (frontend)

**Primary Dependencies**: ASP.NET Core Web API, EF Core 10, Vite, React Router

**Storage**: SQL Server (Azure SQL in production). **No schema change in this feature.** The
per-device view preference lives in browser `localStorage`, not the database.

**Testing**: xUnit (unit + integration with Testcontainers SQL Server), Vitest + React Testing
Library (frontend), Playwright (E2E)

**Target Platform**: Responsive PWA — phone-sized and desktop-sized viewports from one codebase
(Constitution VIII)

**Project Type**: Web application — `backend/` + `frontend/`

**Performance Goals**: No new server round trips for grouping, progress or navigation. Moving
between products in the focused view is a client-side state change and must not refetch the
order.

**Constraints**: Grouping and progress MUST derive only from authoritative imported order data
(`ProductLine`, `Set`, `Quantity`), never from catalog enrichment (FR-006). `localStorage` may
be unavailable or throw (private browsing, blocked site data), so every read and write is
guarded and the size-based default stands in when it fails.

**Scale/Scope**: Orders of roughly 1–40 product lines across 1–6 sets. One new frontend
component tree, one small helper module, one dashboard field. No migration.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Product Owner Authority** | Every requirement traces to PRD v0.4 §8, §10, §12.1, §13, §13.1, §13.2, §18, or to the two clarifications recorded in the spec. PASS |
| **II. No Invented Requirements** | Game ordering and view-preference scope were the two genuine gaps; both were put to the Product Owner rather than assumed. The spec's Out of Scope section names what is deliberately not built. PASS |
| **III. Small, Reviewable Changes** | Three independently shippable user stories, each reviewable alone. US1 changes only how existing data is ordered for display. PASS |
| **IV. Test-Driven Development** | Red → Green → Refactor for every behavioural change. Grouping and progress are pure functions and get unit tests first; the focused view gets RTL tests first; the claim flow and the unresolved-set guard get E2E coverage. The dashboard `activeClaim` field gets an integration test first. PASS |
| **V. Safe Failure Over Silent Corruption** | FR-005 requires a line with a missing or unrecognised set to remain visible rather than be grouped out of existence. FR-011 forbids navigation recording an outcome. `localStorage` failure degrades to the default rather than breaking the view. PASS |
| **VI. Server-Enforced Critical Business Rules** | Claim exclusivity stays entirely server-side and unchanged (013). The dashboard's resume affordance is a convenience; a claim attempt that loses the race is still refused by the server, and FR-027's explanation is presentation over an authoritative answer, not a client-side gate. PASS |
| **VII. Data Minimization** | `activeClaim` is computed server-side against the authenticated employee and returns only that employee's own order. No other employee's identifier is added to any payload. PASS |
| **VIII. One Responsive Product** | Focused and list views are two presentations of one order in one codebase, sharing the same pick and report-issue calls. No duplicated business logic, no second app. PASS |
| **XI. Reliability During Fulfillment** | No new failure mode during picking: navigation is local state, and recording actions are unchanged. Logging is assessed in Phase 1 — the dashboard change is a read, so it warrants none; the claim path already logs (`OrderClaimService`). PASS |
| **XII / XIII. Maintainable Design, Proportional Abstraction** | Grouping is one pure function, not a strategy interface. The focused view is a component, not a framework. No abstraction is introduced for a single implementation. PASS |

**Entity Framework Core Engineering Standards**: the only query touched is the dashboard read
model, which stays a projection to a DTO with `AsNoTracking`, consistent with the existing
dashboard repository.

**Result: PASS — no violations, Complexity Tracking not required.**

## Project Structure

### Documentation (this feature)

```text
specs/016-mobile-picking/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
├── checklists/
│   └── requirements.md  # Spec quality checklist (17/17)
└── spec.md
```

### Source Code (repository root)

```text
backend/
├── src/
│   └── LootSingles.Application/Dashboard/
│       ├── OrderSummary.cs             # + ActiveClaim shape
│       ├── IDashboardRepository.cs     # + active-claim read
│       └── DashboardService.cs         # + pass-through
│   └── LootSingles.Infrastructure/Persistence/
│       └── DashboardRepository.cs      # + projection for the signed-in employee
│   └── LootSingles.Api/Controllers/
│       └── DashboardController.cs      # + field on the response
└── tests/
    ├── LootSingles.UnitTests/
    └── LootSingles.IntegrationTests/   # active-claim projection, incl. NeedsAttention case

frontend/
├── src/features/orders/
│   ├── orderGrouping.ts                # NEW — pure grouping + progress derivation
│   ├── useViewPreference.ts            # NEW — per-device preference, guarded storage
│   ├── FocusedPickView.tsx             # NEW — one product at a time
│   ├── SetTransition.tsx               # NEW — box finished / box unfinished
│   ├── OrderDetailPage.tsx             # view switch, claim action, progress
│   └── ordersApi.ts                    # claim call (endpoint already exists)
├── src/features/dashboard/
│   ├── dashboardApi.ts                 # + activeClaim type
│   └── DashboardPage.tsx               # resume affordance
└── tests/ + e2e/
```

**Structure Decision**: The existing `backend/` + `frontend/` split is unchanged. New frontend
code lives beside the order feature it belongs to rather than in a shared folder, because
nothing outside order picking consumes it. `orderGrouping.ts` is deliberately a plain module of
pure functions — it is the most testable shape and needs no React.

## Phase 0: Research — key decisions

Recorded in [research.md](research.md). In summary:

1. **Grouping computed on the client, not the server.** The payload already carries the fields;
   adding a server-side grouped shape would mean a new contract and a second representation of
   the same order for no behavioural gain.
2. **Per-device preference in `localStorage`.** It is per-origin, per-browser storage, which is
   exactly "per device", and it requires no schema or endpoint. Every access is guarded.
3. **`matchMedia` for the size default**, not user-agent sniffing — the spec says screen size.
4. **`activeClaim` on the dashboard rather than scanning In Progress rows.** An employee can
   hold an order that sits in the Needs Attention section, because reporting an issue retains
   the claim (015). Scanning only In Progress would miss it.
5. **No new pick or issue endpoints.** The focused view calls what feature 015 built.

## Phase 1: Design — outputs

- [data-model.md](data-model.md) — the derived `SetGroup` and `OrderProgress` shapes, the
  `activeClaim` addition, and an explicit statement that no persisted entity changes.
- [contracts/dashboard-api.md](contracts/dashboard-api.md) — the one changed response.
- [contracts/order-grouping.md](contracts/order-grouping.md) — the grouping and progress
  contract as a pure function, since it is this feature's most-tested unit.
- [quickstart.md](quickstart.md) — how to run and validate each user story.

**Post-design Constitution re-check: PASS.** The design adds one nullable response field, one
pure module, and three components. It introduces no interface with a single implementation, no
new persistence, and no duplicated business logic across form factors.
