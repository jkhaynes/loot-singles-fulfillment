# Implementation Plan: Reported Issues on the Card and the List

**Branch**: `018-phone-issue-card` | **Date**: 2026-09-21, amended 2026-09-22 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/018-phone-issue-card/spec.md`

## Summary

A product with a reported issue kept the actions of an untouched one. On the phone the dock read
**Pulled all N**, contradicting the report and silently replacing it when tapped. On the desktop
list the row kept **Picked** beside a **Report Issue** that opened a blank form. This feature gives a
reported product its own state on both views:

- **Phone**: a chip naming the issue, a dock that offers only moving on, and a sheet opened from the
  chip showing what was reported, with **Resolved** and **Edit report**.
- **Desktop**: an issue panel in the row showing what was reported, with **Resolved** and **Edit
  report**, in place of **Picked** and **Report Issue**.

It holds for every issue type. Counts appear in the form's own words ("Required R · Found F") only
when both were recorded, and nothing reads "pulled" or "short".

The work is **frontend only** (research.md §1). The report's details arrive with every order detail,
and both corrections are the existing pick and report calls. A later outcome superseding a report is
feature 015's rule, and the server already enforces it.

### What the amendment changes (2026-09-22)

The phone work is built and committed (`dc5e843`) with the first round's wording. The amendment:

- **rewords** the phone sheet: "Required R · Found F" in place of "F of R pulled" and "N short";
  **Resolved** in place of "I found all N" and "I found it"; **Edit report** in place of "Change
  report";
- **deletes** the `foundActionLabel` helper and its test, since the label is now fixed
  (research.md §7);
- **extracts** the details rendering into `ReportedIssueDetails`, shared by both views
  (research.md §8);
- **adds** the desktop issue panel (research.md §9).

## Technical Context

**Language/Version**: TypeScript 5 / React 19

**Primary Dependencies**: React Router 7, the existing `ordersApi` client. **No new dependencies.**

**Storage**: N/A. Nothing new is stored (data-model.md).

**Testing**: Vitest + React Testing Library for the views, the shared details component and the
form; Playwright for the phone flow and the desktop flow end to end.

**Target Platform**: The phone's single-card view (016) and the desktop order list.

**Project Type**: Web application, the existing `frontend/` layout

**Performance Goals**: None beyond today's. Both views render data already in memory.

**Constraints**:
- Moving between products on the phone must never be blocked or interrupted (016 FR-019a). The sheet
  opens only from the chip.
- No control may state a quantity pulled, and no text may call a report "pulled" or "short"
  (FR-006, FR-011).

**Scale/Scope**:
- Four components touched: `FocusedPickView`, `OrderDetailPage`, `ReportIssueForm`, and their CSS.
- One new component: `ReportedIssueDetails`.
- One helper deleted: `foundActionLabel`.
- No backend files apart from the E2E seed.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Product Owner Authority** | The design is the Product Owner's: direction C for the phone (2026-09-21), and the final mockups for every issue type and the desktop (2026-09-22). All four 2026-09-22 decisions are recorded in the spec's Clarifications, and each supersedes named text rather than sitting beside it. PASS |
| **II. No Invented Requirements** | Every requirement traces to those decisions, PRD §19, or 015/016 rules already in force. The amendment removes behaviour (shortage wording, a quantity-dependent label) rather than adding any. PASS |
| **III. Small, Reviewable Changes** | Frontend only. The rewording, the extraction and the desktop panel are separate task groups, each reviewable alone. PASS |
| **IV. Test-Driven Development** | Red → Green for every behaviour. The first round's phone tests that assert the old wording are **changed to the new wording first**, so they fail for the right reason before the code changes. The shared details component gets its own tests before extraction. The desktop panel's contents, Resolved, Edit report, the read-only panel, and the absence of Picked and Report Issue on a reported row are all tested first, and a desktop E2E covers the flow. PASS |
| **V. Safe Failure Over Silent Corruption** | A correction that fails leaves the report and the row or card as they were, and the picker is told (FR-019, FR-026). Counts missing either half are omitted rather than half-invented (research.md §5). PASS |
| **VI. Server-Enforced Critical Business Rules** | Unchanged. Recording remains claim-gated server-side. The read-only panel and sheet (FR-018, FR-025) are presentation over rules the server already enforces. PASS |
| **VII. Data Minimization** | No customer data is involved. Both views show the employee who reported the issue, which the desktop view already did. PASS |
| **VIII. One Responsive Product** | One set of rules for both views, stated once in `ReportedIssueDetails`. One form for first reports and edits on both views. PASS |
| **XI. Reliability During Fulfillment** | No new failure mode, and no new request. Logging: nothing new warrants it, because the pick and report calls are already logged server-side. PASS |
| **XII / XIII. Maintainable Design, Proportional Abstraction** | One shared component, extracted because two views must follow the same rules (research.md §8), not in anticipation. One helper deleted because its rule is gone (research.md §7). The desktop row keeps using its existing state and handlers rather than being refactored (research.md §9). PASS |
| **EF Core Standards** | Not applicable; no data access changes. PASS |

## Project Structure

### Documentation (this feature)

```text
specs/018-phone-issue-card/
├── plan.md              # This file
├── spec.md              # Feature specification, amended 2026-09-22
├── research.md          # Ten decisions (§5 and §7 revised, §8–§10 added)
├── data-model.md        # No changes; the fields shown, and the phone and desktop states
├── quickstart.md        # Manual validation: phone, desktop, unchanged
├── contracts/
│   └── card-ui.md       # The UI contract tests assert against, both views
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # T001–T023 done; the amendment's tasks are added by /speckit-tasks
```

### Source Code (repository root)

```text
frontend/src/features/orders/
├── ReportedIssueDetails.tsx  # new: a report's details, shared by both views (research.md §8)
├── FocusedPickView.tsx       # the sheet: new wording, Resolved, Edit report, uses the shared details
├── OrderDetailPage.tsx       # the desktop issue panel in a reported row
├── ReportIssueForm.tsx       # unchanged; `initial` already exists
├── foundActionLabel.ts       # deleted (research.md §7)
└── OrderDetailPage.css       # the desktop issue panel's styles, from the existing tokens

frontend/tests/orders/
├── ReportedIssueDetails.test.tsx  # new: the four omission rules and the wording, once
├── FocusedPickView.test.tsx       # first-round sheet tests reworded first, then green
├── OrderDetailPage.test.tsx       # new desktop panel tests; existing ones stay green (research.md §10)
└── foundActionLabel.test.ts       # deleted with the helper

frontend/e2e/
├── mobile-issue-card.spec.ts      # reworded to the new labels and counts
└── desktop-issue-panel.spec.ts    # new: the panel, Edit report, Resolved, read-only

backend/tests/LootSingles.E2EHost/Program.cs
                                   # + one order and one picker for the desktop spec
```

**Structure Decision**: The existing web-application layout. All product code is in
`frontend/src/features/orders/`, beside the views it changes. The one backend file touched is the
E2E host's seed. The suite runs fully parallel, so the desktop spec needs an order and picker of its
own.

## Risks

- **Tests that encode the old wording.** The first round's RTL tests and phone E2E assert "3 of 4
  pulled", "1 short", "I found all 4" and "Change report". They are reworded before the code, so the
  red phase fails on the old behaviour rather than silently passing on it.
- **The desktop row is dense.** Hiding Picked and Report Issue on a reported row must not disturb
  rows without a report. FR-027 is pinned by tests that pass before and after.
- **"Resolved" does not repeat the quantity.** The Product Owner accepted this. The quantity stays
  prominent on the card and the row, and the action is unchanged: it records the product as picked.

## Complexity Tracking

No constitution violations to justify.
