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
/speckit-tasks
```
```
/speckit-checklist   (optional quality gate)
/speckit-analyze     (optional quality gate)
```

→ review and approve `tasks.md` (the tracker; no GitHub issue needed) →

```
/speckit-implement
/speckit-converge
```

Repeat `/speckit-implement` → `/speckit-converge` until convergence is reported — that loop, not a single implement pass, is what "done" means for a feature.

The Spec Kit `git` extension branches and commits along the way automatically (branch created before `/speckit-specify`; each planning command offers a confirmed commit after it runs). Config: `.specify/extensions.yml` and `.specify/extensions/git/git-config.yml`.

## Roles

### Product Owner

The Loot Card Shop owner. Validates business workflows and requirements, and is the final authority on product decisions.

### Developer

Owns technical decisions, code review, architecture, and approval of specifications and plans before implementation begins.

### Spec Kit

Owns the complete AI-assisted feature lifecycle: constitution, feature specification, clarification, technical planning, task breakdown, artifact analysis, implementation, and convergence verification. No second execution methodology runs alongside it. Strict Test-Driven Development (Red → Green → Refactor) is enforced directly through the project constitution's Principle IV during implementation, not by a separate tool.

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
5.  Spec Kit task breakdown             (/speckit-tasks)
6.  Human review / approval        (tasks.md is the tracker — no GitHub issue required)
7.  Spec Kit implementation             (/speckit-implement) — strict TDD per constitution Principle IV
8.  Spec Kit convergence verification   (/speckit-converge) — repeat 7-8 until converged
9.  Pull request, CI, human review, merge
```

Spec Kit artifacts (specification, plan, tasks) are the implementation contract for a feature. `/speckit-implement` executes against that contract; it does not originate or silently amend it. If execution surfaces a contradiction, missing rule, or architectural conflict, work returns to Spec Kit clarification/planning rather than being resolved ad hoc during implementation — see the source-of-truth hierarchy in [`CLAUDE.md`](../../CLAUDE.md).

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
