# Implementation Plan: Order Import UI

**Branch**: `004-order-import-ui` | **Date**: 2026-08-21 | **Spec**: [spec.md](spec.md)

## Summary

Add authenticated import and order-browse screens to the React PWA. A multipart `POST /api/imports` adapts the uploaded stream to the existing `IPackingSlipImportService` and streams typed newline-delimited JSON progress snapshots in the same request, emitting only after an order finishes and at terminal completion. Request cancellation stops remaining work while preserving completed per-order transactions; database uniqueness makes re-upload safe. A read-only `GET /api/orders` projects each persisted order's identifier, actual status, and import time without loading lines or exposing shipping data.

## Technical Context

**Language/Version**: C# 14 / .NET 10; TypeScript 6.0 / React 19

**Primary Dependencies**: ASP.NET Core, EF Core 10/SQL Server, existing import service, React Router 7, native Fetch streams, existing CSS tokens

**Storage**: Existing Orders/OrderLines/ImportAttempts/ImportOrderResults; no schema change or PDF retention

**Testing**: xUnit; Vitest + React Testing Library; Playwright

**Target Platform**: ASP.NET Core API and one responsive browser PWA

**Project Type**: Layered .NET/React web application

**Performance Goals**: Visible progress for ~200 orders; results within 5 seconds after completion; browse lookup within 10 seconds

**Constraints**: One PDF/request with a server-enforced 25 MB maximum; any authenticated role; synchronous request; propagate HTTP abort cancellation; completed per-order transactions survive interruption or deliberate cancellation; retry cannot duplicate orders; explicit `InProgress`/`Completed`/`Failed` transport status and client-derived `Interrupted`/`Cancelled`; Failed, Interrupted, and Cancelled retain clearly qualified partial results; no customer shipping data; no mobile horizontal scroll

**Scale/Scope**: Single-business internal tool; flat, sorted, unpaginated V1 order list

## Constitution Check

*GATE: Passed before Phase 0; re-checked after Phase 1.*

| Principle | Design evidence | Status |
|---|---|---|
| I-II Authority/scope | Implements clarified spec 004 only; no search, history, jobs, or filters | PASS |
| III Small changes | Isolated feature branch reusing features 001-003 | PASS |
| IV TDD | xUnit, Vitest/RTL, and Playwright Red-Green-Refactor coverage planned | PASS (execution gate) |
| V Safe failure | Typed codes/status remain authoritative; terminal record required; Failed/Interrupted/Cancelled partial results are clearly qualified and never reported as completion | PASS |
| VI Server enforcement | Both endpoints require server-side authorization; import service/database retain duplicate guarantees | PASS |
| VII Data minimization | Request stream is not retained; DTOs contain no shipping data | PASS |
| VIII Responsive product | One React implementation adapts presentation across viewports | PASS |
| IX Replaceable integrations | HTTP adapter depends on the source-neutral existing application service | PASS |
| X Single business | No tenant/general upload platform | PASS |
| XI Reliability | Abort cancels remaining work, preserves committed orders, and supports duplicate-safe retry; unexpected missing terminal state becomes Interrupted while an intentional abort becomes Cancelled | PASS |
| XII Maintainability | HTTP adaptation, aggregate-aligned order service/repository, stream decoder, and pages have clear responsibilities | PASS |
| XIII Simplicity | Native multipart/NDJSON/Fetch; no jobs, polling, WebSockets, state library, or generic framework | PASS |
| EF Core standards | Async `AsNoTracking` server projection, ordered before materialization, no line loading | PASS |

No violations require justification.

**Architecture/changeability review**: `ImportsController` only maps authenticated HTTP input/output and delegates all parsing, validation, and persistence. It enforces a 25 MB file limit before invoking import processing (with the multipart request cap configured high enough for boundary overhead) and passes `HttpContext.RequestAborted` through `ImportAsync`; cancellation stops future work but does not roll back earlier per-order transactions. Its transport adapter coalesces service updates by `OrdersProcessed`, emits `InProgress` when that count increases, and emits terminal `Completed` or `Failed` when the connection remains writable. The existing database uniqueness constraint and typed duplicate handling make a retry converge without duplicate Orders. `OrdersService` owns application operations for the `Order` aggregate, while `IOrderRepository`/`OrderRepository` form its persistence boundary. The frontend decoder owns framing: `Failed` retains completed results with an explicit failed label; an unexpected EOF without terminal `Completed`/`Failed` derives `Interrupted`; an employee-initiated abort derives `Cancelled`. The import page owns one `AbortController` per submission/retry, guards application and browser-history navigation, uses the native `beforeunload` warning for refresh/tab close, aborts before confirmed navigation, and retains qualified partial state for on-page cancellation. All non-completed states explain that some orders may remain imported and enable retry. Expected failures use stable codes, not message inspection. No new persisted entity, batch rollback, or generic streaming abstraction is justified.

**Post-Phase 1 re-check**: [research.md](research.md), [data-model.md](data-model.md), and both contracts preserve these boundaries, approved fields, authorization, no-retention, explicit completion, and efficient projection. All gates remain PASS.

## Project Structure

```text
specs/004-order-import-ui/
|-- plan.md
|-- research.md
|-- data-model.md
|-- quickstart.md
`-- contracts/{import-api.md,orders-api.md}

backend/src/
|-- LootSingles.Application/Orders/{IOrderRepository.cs,OrderListItem.cs,OrdersService.cs}
|-- LootSingles.Infrastructure/Persistence/OrderRepository.cs
`-- LootSingles.Api/Controllers/{ImportsController.cs,OrdersController.cs}
backend/tests/
|-- LootSingles.UnitTests/Orders/
`-- LootSingles.IntegrationTests/{ImportUi,Orders}/

frontend/src/features/
|-- import/{importApi.ts,ImportPage.tsx,ImportPage.css}
`-- orders/{ordersApi.ts,OrdersPage.tsx,OrdersPage.css}
frontend/tests/{import,orders}/
frontend/e2e/order-import.spec.ts
```

**Structure Decision**: Preserve the established layered backend and feature-folder frontend. The existing import service is unchanged behind a thin API adapter. `OrdersService` owns cohesive application operations for the `Order` aggregate and uses one aggregate-aligned `IOrderRepository`; its current listing method calls a projected, no-tracking repository query. Authenticated navigation uses the installed router.

## Complexity Tracking

No constitution violations to track.
