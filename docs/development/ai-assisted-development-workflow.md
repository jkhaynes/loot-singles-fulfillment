# AI-Assisted Development Workflow

This document explains how AI-assisted development works on Loot Singles Fulfillment, for any engineer joining the project without prior context.

## Purpose

AI accelerates development but is not an autonomous source of product requirements. Every implementation decision must trace back to a human-approved source of truth. This workflow exists to keep AI-assisted speed from silently turning into unapproved scope.

## Tracks

Pick the track by the size of the change:

- **Feature** (new behavior, several files, worth remembering why): Spec Kit designs it, then Superpowers implements it. The full flow is below.
- **Small change** (bug fix, tweak, or refactor with no new user-visible behavior): no spec. Use Superpowers directly: `systematic-debugging` for bugs, `test-driven-development` for everything else. Anything that adds user-visible behavior is a feature and needs a spec.
- **Spike / experiment**: Superpowers `brainstorming` on a throwaway branch, no spec. If it survives, promote it to a feature.

Strict TDD (Red → Green → Refactor, constitution Principle IV) applies to every track.

## Command Cheat Sheet (feature track)

Design, with Spec Kit:

```
/speckit-specify <feature description>
/speckit-clarify
/speckit-plan
```

→ human architecture and changeability review (required — see constitution "Architecture and Changeability Review") →

```
/speckit-checklist   (optional quality gate, when useful)
/speckit-tasks
/speckit-analyze
```

→ human approval of `tasks.md` (the tracker; no GitHub issue needed) →

Implementation, with Superpowers, on the feature branch in the main checkout (no git worktrees):

```
subagent-driven-development        one fresh subagent per task in tasks.md: test first, then
                                   a spec-compliance review and a code-quality review
requesting-code-review             before merge; fix Critical and Important findings test-first
finishing-a-development-branch     (or /ship) → pull request → CI → human review → merge
```

Do not run `/speckit-implement`, `/branch-review`, `/review-remediation` or `/speckit-converge`, and do not run Superpowers `brainstorming` or `writing-plans` on a Spec Kit feature: `tasks.md` is the plan.

The Spec Kit `git` extension branches and commits along the way automatically (branch created before `/speckit-specify`; each planning command offers a confirmed commit after it runs). Config: `.specify/extensions.yml` and `.specify/extensions/git/git-config.yml`.

## Roles

### Product Owner

The Loot Card Shop owner. Validates business workflows and requirements, and is the final authority on product decisions.

### Developer

Owns technical decisions, code review, architecture, and approval of specifications and plans before implementation begins.

### Spec Kit

Owns feature design: constitution, feature specification, clarification, technical planning, task breakdown, and artifact analysis.

### Superpowers

Owns implementation: carrying out the approved `tasks.md` test-first with one subagent per task, per-task spec-compliance and code-quality review, code review before merge, and finishing the branch. It also runs the small-change and spike tracks.

### Claude Code

Acts as the AI implementation agent, operating under the constraints in [`CLAUDE.md`](../../CLAUDE.md), Spec Kit, and Superpowers.

## Source of Truth Hierarchy

See the hierarchy defined in [`CLAUDE.md`](../../CLAUDE.md). When artifacts conflict, work stops for clarification rather than silently resolving in favor of the lower-level artifact.

## Full Workflow (feature track)

```text
1.  Product Owner feedback or approved PRD requirement
2.  Spec Kit feature specification      (/speckit-specify)
3.  Spec Kit clarification              (/speckit-clarify)
4.  Spec Kit technical plan             (/speckit-plan)
5.  Human architecture and changeability review   (required — see constitution "Architecture and Changeability Review")
6.  Spec Kit checklist                  (/speckit-checklist) — optional quality gate, when useful
7.  Spec Kit task breakdown             (/speckit-tasks)
8.  Spec Kit artifact analysis          (/speckit-analyze)
9.  Human review / approval        (tasks.md is the tracker — no GitHub issue required)
10. Superpowers implementation          (subagent-driven-development) — one subagent per task, strict TDD per constitution Principle IV, spec-compliance then code-quality review per task
11. Automated build/tests pass
12. Code review                         (requesting-code-review) — fix Critical and Important findings test-first; a round with none is the stopping point
13. Finish the branch                   (finishing-a-development-branch or /ship) — pull request, CI, human review, merge
```

Spec Kit artifacts (specification, plan, tasks) are the implementation contract for a feature. Superpowers executes against that contract; it does not originate or silently amend it. If implementation or review surfaces a contradiction, missing rule, or architectural conflict, work returns to Spec Kit: a flawed plan to `/speckit-plan`, an unresolved or contradictory requirement to `/speckit-clarify`. It is never resolved ad hoc during implementation — see the source-of-truth hierarchy in [`CLAUDE.md`](../../CLAUDE.md).

## Code Review

| Step | Evaluates | When |
|---|---|---|
| Human architecture/changeability review | The *proposed design* | After `/speckit-plan`, before task breakdown |
| Per-task reviews (subagent-driven-development) | Each task against the spec, then its code quality | As each task lands |
| requesting-code-review | The whole branch against the approved spec, plan and constitution | Before merge, once local build/tests pass |

Critical and Important findings are fixed before merge; behavioral fixes are test-first (the regression test that shows the defect comes before the fix). Minor findings are the Developer's call. Review rounds have diminishing returns, and remediation can introduce its own defects, so a round with no Critical or Important findings is the stopping point.

## Feature Branches

No product feature work is committed directly to `main`. Each feature is developed on its own branch in the main checkout (no git worktrees, so the owner can follow along in their own editor) and merged via pull request after review and CI. A merge to `main` deploys to stage.

## Testing

Planned stack:

- **xUnit** — backend unit/integration tests
- **Vitest** and **React Testing Library** — frontend unit/component tests
- **Playwright** — critical end-to-end workflows

## Project-Specific Validation Gates

These gates apply in addition to normal code review, and reflect the highest-risk failure modes identified in the PRD.

### TCGplayer Import Changes

Must be tested against representative, sanitized packing-slip fixtures. Parser uncertainty must fail safely — surface an import problem rather than silently creating an incomplete or incorrect order.

### Card Enrichment Changes

Tests must prove that ambiguous or unconfident catalog matches do not display a guessed image. "No image is better than the wrong image" is a hard product rule, not a UX preference.

### Order Claiming Changes

Must include concurrency testing demonstrating that two employees cannot successfully claim the same order.

### Quantity UX

Quantity greater than one must be represented correctly and prominently through import, persistence, API, and UI — end to end, not just at the point of display.

### Picker UI

Critical picking flows must receive Playwright/browser validation on responsive viewport sizes representing both desktop and mobile phone use.
