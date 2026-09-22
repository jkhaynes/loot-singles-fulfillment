# Implementation Plan: Reported Issues on the Phone's Card

**Branch**: `018-phone-issue-card` | **Date**: 2026-09-21 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/018-phone-issue-card/spec.md`

## Summary

On the phone's single-card view, a product with a reported issue currently keeps the actions of an
untouched product, including a **Pulled all N** button that contradicts the report and silently
replaces it when tapped. This feature gives a reported product its own state:
- a chip on the card naming the issue;
- a dock that offers only moving on;
- a sheet, opened from the chip, that shows what was reported and holds the two corrections.

The work is **frontend only** (research.md §1). The report's counts, note and reporter already
arrive with every order detail, and both corrections are the existing pick and report calls. A
later outcome superseding a report is feature 015's rule, and the server already enforces it.

## Technical Context

**Language/Version**: TypeScript 5 / React 19

**Primary Dependencies**: React Router 7, the existing `ordersApi` client. **No new dependencies.**

**Storage**: N/A. Nothing new is stored (data-model.md).

**Testing**: Vitest + React Testing Library for the view, the form and the label helper; Playwright
for the phone flow end to end.

**Target Platform**: The phone's single-card view (016). The desktop view is out of scope (FR-021).

**Project Type**: Web application, the existing `frontend/` layout

**Performance Goals**: None beyond today's. The sheet renders data already in memory.

**Constraints**:
- Moving between products must never be blocked or interrupted (016 FR-019a). The sheet opens
  only from the chip.
- No control may state a quantity pulled that contradicts the report (FR-006).

**Scale/Scope**: Three components touched (`FocusedPickView`, `ReportIssueForm`, their CSS), one new
pure helper. No backend files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Product Owner Authority** | The design is the Product Owner's choice of direction C (2026-09-21), and the spec records it. The three details the mockup left open are recorded as assumptions for `/speckit-clarify` to overturn, not decided silently. PASS |
| **II. No Invented Requirements** | Every requirement traces to the chosen design, PRD §19, or 015/016 rules already in force. The only new wording ("I found it" for a quantity of one) exists so a quantity of one isn't stated as "all 1". PASS |
| **III. Small, Reviewable Changes** | Frontend only, three components, no backend. Each user story is reviewable alone. PASS |
| **IV. Test-Driven Development** | Red → Green for every behaviour. RTL tests are written first for the chip, the issue dock, the sheet's contents and omissions, the found action, Change report pre-fill and cancel, the read-only sheet, and a failed correction. A unit test covers `foundActionLabel`. The E2E test covers the reported flow on a phone. Existing tests with an ambiguous `/next card/i` locator are made exact **before** the dock changes, so they fail for the right reason (research.md §6). PASS |
| **V. Safe Failure Over Silent Corruption** | A correction that fails leaves the report and the card as they were, and the picker is told (FR-019). The sheet stays up until the line actually changes, so it never claims a correction happened (research.md §2). The partial-counts rule omits the quantity rather than inventing half of it (research.md §5). PASS |
| **VI. Server-Enforced Critical Business Rules** | Unchanged. Recording remains claim-gated server-side. The sheet's read-only form (FR-018) is presentation over rules the server already enforces. PASS |
| **VII. Data Minimization** | No customer data is involved. The sheet shows the employee who reported the issue, which the desktop view already shows. PASS |
| **VIII. One Responsive Product** | Same product, same view; the phone card gains a state. The shared issue form stays shared (research.md §4). PASS |
| **XI. Reliability During Fulfillment** | No new failure mode, and no new request. Logging: nothing new warrants it, because the pick and report calls are already logged server-side. PASS |
| **XII / XIII. Maintainable Design, Proportional Abstraction** | One optional prop on an existing form instead of a second form. One pure helper for the one worded rule. No modal library; a plain overlay, because jsdom cannot run `showModal()` and the application has no accessibility scope (research.md §3). PASS |
| **EF Core Standards** | Not applicable; no data access changes. PASS |

## Project Structure

### Documentation (this feature)

```text
specs/018-phone-issue-card/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0: seven decisions
├── data-model.md        # Phase 1: no changes; the fields shown and the card states
├── quickstart.md        # Phase 1: manual validation scenarios
├── contracts/
│   └── card-ui.md       # Phase 1: the UI contract tests assert against
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Created by /speckit-tasks, not here
```

### Source Code (repository root)

```text
frontend/src/features/orders/
├── FocusedPickView.tsx       # the chip, the issue dock, the sheet and its local state
├── ReportIssueForm.tsx       # + optional `initial` report to pre-fill from (Change report)
├── foundActionLabel.ts       # new: "I found it" / "I found all N"
└── OrderDetailPage.css       # chip, issue dock and sheet styles, from the existing tokens

frontend/tests/orders/
├── FocusedPickView.test.tsx  # the reported state, the sheet, the corrections, read-only
├── ReportIssueForm.test.tsx  # new: pre-fill from `initial`; blank without it
├── foundActionLabel.test.ts  # new
└── OrderDetailPage.test.tsx  # BR-002 test: exact "Next card" locator (research.md §6)

frontend/e2e/
├── mobile-issue-card.spec.ts # new: report, see, correct, on a phone
└── pick-handoff.spec.ts      # held-order test: exact "Next card" locator (research.md §6)

backend/tests/LootSingles.E2EHost/Program.cs
                              # + one order (a quantity-4 product) and one picker for the new spec
```

**Structure Decision**: The existing web-application layout. All product code is in
`frontend/src/features/orders/`, beside the view it changes. The one backend file touched is the
E2E host's seed. The suite runs fully parallel, so the new spec needs an order and picker of its
own.

## Risks

- **Ambiguous locators** (research.md §6). The new primary button and the › arrow both match
  `/next card/i`. Tests that move on after reporting must use exact names, and they are updated
  first so the red phase is honest.
- **The found action replaces a report.** That's the rule since 015, and it is the point of the
  action. It now needs two deliberate taps (the chip, then the action) instead of one tap on a
  mislabelled dock button. That trade is the feature, not a risk to engineer around.

## Complexity Tracking

No constitution violations to justify.
