# AI-Assisted Development Workflow

This document explains how AI-assisted development works on Loot Singles Fulfillment, for any engineer joining the project without prior context.

## Purpose

AI accelerates development but is not an autonomous source of product requirements. Every implementation decision must trace back to a human-approved source of truth. This workflow exists to keep AI-assisted speed from silently turning into unapproved scope.

## Roles

### Product Owner

The Loot Card Shop owner. Validates business workflows and requirements, and is the final authority on product decisions.

### Developer

Owns technical decisions, code review, architecture, and approval of specifications and plans before implementation begins.

### Spec Kit

Owns specification and planning: constitution, feature specification, clarification, technical planning, and task breakdown.

### Superpowers

Owns disciplined execution and verification: test-driven development, executing approved tasks, systematic debugging, verification before completion, and code quality review.

### Claude Code

Acts as the AI implementation agent, operating under the constraints in [`CLAUDE.md`](../../CLAUDE.md), Spec Kit, and Superpowers.

## Source of Truth Hierarchy

See the hierarchy defined in [`CLAUDE.md`](../../CLAUDE.md). When artifacts conflict, work stops for clarification rather than silently resolving in favor of the lower-level artifact.

## Full Workflow

```text
1.  Product Owner feedback or approved PRD requirement
2.  Spec Kit feature specification      (/speckit.specify)
3.  Spec Kit clarification              (/speckit.clarify)
4.  Spec Kit technical plan             (/speckit.plan)
5.  Spec Kit task breakdown             (/speckit.tasks)
6.  Human review / approval
7.  Create GitHub issue
8.  Create feature branch/worktree
9.  Superpowers executes approved tasks (TDD)
10. Tests are written and run
11. Systematic debugging if failures occur
12. Verify implementation against the approved specification
13. Code quality review
14. Pull request
15. CI
16. Human review
17. Merge
```

Spec Kit artifacts (specification, plan, tasks) are the implementation contract for a feature. Superpowers executes against that contract; it does not originate or silently amend it. If execution surfaces a contradiction, missing rule, or architectural conflict, work returns to Spec Kit clarification/planning rather than being resolved ad hoc during implementation.

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
