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
/branch-review
```

→ turn findings into tasks with `/review-remediation` (all Required findings, plus whichever Optional ones you approve), resolve them via `/speckit-implement`, then re-run `/branch-review` — repeat until no Required findings remain →

```
/speckit-converge
```

→ pull request → CI → human review → merge

Repeat `/speckit-implement` → `/speckit-converge` until convergence is reported — that loop, not a single implement pass, is what "done" means for a feature. `/branch-review` sits inside that loop too: it must be clean (zero Required findings) before `/speckit-converge` runs.

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
12. Branch review                       (/branch-review + /review-remediation) — evaluates the actual implementation; repeat 10-12 until zero Required findings remain
13. Spec Kit convergence verification   (/speckit-converge) — repeat 10-13 until converged
14. Pull request, CI, human review, merge
```

Spec Kit artifacts (specification, plan, tasks) are the implementation contract for a feature. `/speckit-implement` executes against that contract; it does not originate or silently amend it. If execution or `/branch-review` surfaces a contradiction, missing rule, or architectural conflict, work returns to Spec Kit clarification/planning rather than being resolved ad hoc during implementation — see the source-of-truth hierarchy in [`CLAUDE.md`](../../CLAUDE.md).

## Branch Review Gate

`/branch-review` is Spec Kit's post-implementation review gate — it is not a second development methodology, and it does not replace human PR review. It reviews the current branch against its base branch, so run it on the feature branch.

| Step | Evaluates | When |
|---|---|---|
| Human architecture/changeability review | The *proposed design* | After `/speckit-plan`, before task breakdown |
| `/branch-review` | The *actual implementation* | After `/speckit-implement`, once local build/tests pass |
| `/speckit-converge` | Final conformance to the approved spec/plan/tasks | After review is clean, before PR |

`/branch-review` classifies each finding as **Required** or **Optional**, separately from its severity (a Low-severity finding may still be Required), and returns one verdict: PASS, PASS WITH SUGGESTIONS, or CHANGES REQUESTED.

- **Required** findings block completion.
- **Optional** findings do not block completion. Each carries an advisory `Recommended: Yes/No`; whether to take one on is the Product Owner's call.
- `/review-remediation` converts findings into tasks: every unresolved Required finding automatically, plus whichever Optional ones you approve. It appends them to the feature's existing `tasks.md` — never a separate or competing task-tracking system — and plans the work without implementing it. Behavioral fixes are ordered test-first: the regression test that demonstrates the defect comes before the fix.
- Resolve those tasks with `/speckit-implement`, then re-run `/branch-review`; repeat until no Required findings remain.
- A Required finding that traces back to a flawed technical plan means returning to `/speckit-plan`, not patching around the plan in code.
- A Required finding that traces back to an unresolved or contradictory requirement means returning to `/speckit-clarify`.
- Review rounds have diminishing returns, and remediation can introduce its own defects. A round with no Required findings is the stopping point.

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
