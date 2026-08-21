# AI-Assisted Development Workflow

This document explains how AI-assisted development works on Loot Singles Fulfillment, for any engineer joining the project without prior context.

## Purpose

AI accelerates development but is not an autonomous source of product requirements. Every implementation decision must trace back to a human-approved source of truth. This workflow exists to keep AI-assisted speed from silently turning into unapproved scope.

## Command Cheat Sheet

What to actually type, in order:

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

```
/speckit-implement
```

→ automated build/tests pass →

```
/code-design-review
```

→ remediate any Must Fix findings as new tasks in the existing `tasks.md`, via `/speckit-implement`, then re-run `/code-design-review` — repeat until no Must Fix findings remain →

```
/speckit-converge
```

→ pull request → CI → human review → merge

Repeat `/speckit-implement` → `/speckit-converge` until convergence is reported — that loop, not a single implement pass, is what "done" means for a feature. `/code-design-review` sits inside that loop too: it must be clean (zero Must Fix findings) before `/speckit-converge` runs.

The Spec Kit `git` extension branches and commits along the way automatically (branch created before `/speckit-specify`; each planning command offers a confirmed commit after it runs). Config: `.specify/extensions.yml` and `.specify/extensions/git/git-config.yml`.

## Roles

### Product Owner

The Loot Card Shop owner. Validates business workflows and requirements, and is the final authority on product decisions.

### Developer

Owns technical decisions, code review, architecture, and approval of specifications and plans before implementation begins.

### Spec Kit

Owns the complete AI-assisted feature lifecycle: constitution, feature specification, clarification, technical planning, task breakdown, artifact analysis, implementation, code and design review, and convergence verification. No second execution methodology runs alongside it. Strict Test-Driven Development (Red → Green → Refactor) is enforced directly through the project constitution's Principle IV during implementation, not by a separate tool.

### Claude Code

Acts as the AI implementation agent, operating under the constraints in [`CLAUDE.md`](../../CLAUDE.md) and Spec Kit.

## Source of Truth Hierarchy

See the hierarchy defined in [`CLAUDE.md`](../../CLAUDE.md). When artifacts conflict, work stops for clarification rather than silently resolving in favor of the lower-level artifact.

## Full Workflow

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
10. Spec Kit implementation             (/speckit-implement) — strict TDD per constitution Principle IV
11. Automated build/tests pass
12. Code and Design Review              (/code-design-review) — evaluates the actual implementation; repeat 10-12 until zero Must Fix findings remain
13. Spec Kit convergence verification   (/speckit-converge) — repeat 10-13 until converged
14. Pull request, CI, human review, merge
```

Spec Kit artifacts (specification, plan, tasks) are the implementation contract for a feature. `/speckit-implement` executes against that contract; it does not originate or silently amend it. If execution or `/code-design-review` surfaces a contradiction, missing rule, or architectural conflict, work returns to Spec Kit clarification/planning rather than being resolved ad hoc during implementation — see the source-of-truth hierarchy in [`CLAUDE.md`](../../CLAUDE.md).

## Code and Design Review Gate

`/code-design-review` is Spec Kit's post-implementation review gate — it is not a second development methodology, and it does not replace human PR review.

| Step | Evaluates | When |
|---|---|---|
| Human architecture/changeability review | The *proposed design* | After `/speckit-plan`, before task breakdown |
| `/code-design-review` | The *actual implementation* | After `/speckit-implement`, once local build/tests pass |
| `/speckit-converge` | Final conformance to the approved spec/plan/tasks | After review is clean, before PR |

`/code-design-review` classifies findings as **Must Fix** or **Advisory**:

- **Must Fix** findings are added as new tasks to the feature's existing `tasks.md` — never a separate or competing task-tracking system — and are resolved by running `/speckit-implement` again. `/code-design-review` is then re-run; repeat until no Must Fix findings remain.
- **Advisory** findings (optional improvements, subjective preferences, speculative optimizations) do not block completion.
- A Must Fix finding that traces back to a flawed technical plan means returning to `/speckit-plan`, not patching around the plan in code.
- A Must Fix finding that traces back to an unresolved or contradictory requirement means returning to `/speckit-clarify`.

This gate applies to application-code changes and is required before `/speckit-converge`. It does not apply to documentation-only or configuration-only changes.

## Feature Branches

No product feature work is committed directly to `main`. Each feature is developed on its own branch (or worktree) and merged via pull request after review and CI.

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
