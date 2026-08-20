# AI-Assisted Development Workflow

This document explains how AI-assisted development works on Loot Singles Fulfillment, for any engineer joining the project without prior context.

## Purpose

AI accelerates development but is not an autonomous source of product requirements. Every implementation decision must trace back to a human-approved source of truth. This workflow exists to keep AI-assisted speed from silently turning into unapproved scope.

## Command Cheat Sheet

What to actually type, in order.

**1. Planning (Spec Kit slash commands, run once per feature):**
```
/speckit-specify <feature description>
/speckit-clarify
/speckit-plan
/speckit-tasks
```
Optional quality gates between tasks and implementation:
```
/speckit-checklist
/speckit-analyze
```
The Spec Kit `git` extension is installed and handles branching/committing for this pipeline automatically — no extra commands needed:
- `/speckit-specify` creates and switches to a new feature branch before spec work starts (`before_specify` hook, mandatory).
- `/speckit-specify`, `/speckit-clarify`, `/speckit-plan`, and `/speckit-tasks` each auto-commit their output immediately after running (`after_*` hooks, mandatory), so work is checked in between every planning step rather than accumulating into one large diff.
- `implement`/`checklist`/`analyze`/`taskstoissues` commit hooks are left as prompted/optional — Superpowers owns commit discipline during implementation (TDD commits, `finishing-a-development-branch`), so auto-committing there is not enabled.
- Config lives in `.specify/extensions.yml` (hook wiring) and `.specify/extensions/git/git-config.yml` (branch naming, commit messages, per-event enable/disable).

**2. Human checkpoint (not a command):** review and approve `tasks.md`. This is the tracker for in-progress work — no GitHub issue is required per feature.

**3. Implementation — no dedicated slash command exists for this step.** Superpowers skills auto-trigger off plain instructions rather than shipping `/superpowers-*` commands. Say:
```
Implement tasks.md
```
Claude is required (via `using-superpowers`) to invoke the right skill chain itself — `using-git-worktrees` → `subagent-driven-development` (or `executing-plans` for a single session) → per-task TDD/debugging/review → `verification-before-completion` → `finishing-a-development-branch`. To force a specific execution mode instead of letting Claude choose:
```
Use superpowers:subagent-driven-development to execute tasks.md
```

**4. End of implementation (a prompt you answer, not a command you initiate):** `finishing-a-development-branch` asks you to choose merge locally / push+PR / keep as-is.

**Do not use `/speckit-implement`.** Spec Kit ships this as a generic task-runner, but it bypasses Superpowers' TDD/debugging/review discipline, which conflicts with the Spec Kit/Superpowers boundary in [`CLAUDE.md`](../../CLAUDE.md). Plain "Implement tasks.md" is the correct trigger for this project, not `/speckit-implement`.

## Roles

### Product Owner

The Loot Card Shop owner. Validates business workflows and requirements, and is the final authority on product decisions.

### Developer

Owns technical decisions, code review, architecture, and approval of specifications and plans before implementation begins.

### Spec Kit

Owns specification and planning: constitution, feature specification, clarification, technical planning, and task breakdown.

### Superpowers

Owns disciplined execution and verification, via a fixed set of skills invoked in sequence for every feature: isolate the workspace, execute tasks test-first, debug systematically on failure, verify with evidence before any completion claim, get an independent code review, and close out the branch. See [Superpowers Execution In Detail](#superpowers-execution-in-detail) below for what each skill actually does.

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
6.  Human review / approval        (tasks.md is the tracker — no GitHub issue required)
7.  Superpowers takes over execution — see the detailed breakdown below
```

Spec Kit artifacts (specification, plan, tasks) are the implementation contract for a feature. Superpowers executes against that contract; it does not originate or silently amend it. If execution surfaces a contradiction, missing rule, or architectural conflict, work returns to Spec Kit clarification/planning rather than being resolved ad hoc during implementation.

## Superpowers Execution In Detail

Step 7 above ("Superpowers takes over execution") is not one action, it is a fixed sequence of named skills. Each skill has its own enforcement rules (an "Iron Law") that Claude Code cannot skip by rationalizing — e.g. "tests after achieve the same purpose" or "linter passed" are explicitly called out as invalid excuses in the skill definitions themselves. This section documents what actually happens, not a paraphrase of it.

```text
7a. using-git-worktrees        — isolate this feature's work from other branches
7b. subagent-driven-development — for independent, plan-listed tasks (preferred);
    or executing-plans          — for a single continuous session
7c. Per task, repeated for every task in the breakdown:
      i.   test-driven-development
           RED:    write one failing test, run it, confirm it fails for the
                    right reason (not a typo)
           GREEN:  write the minimal code to pass it, run it, confirm all
                    tests pass
           REFACTOR: clean up only with tests staying green
           Iron law: no production code without a failing test written first
      ii.  systematic-debugging (only if a test/build/behavior fails
           unexpectedly)
           Phase 1: root cause investigation (read the error, reproduce it,
                     check recent changes, trace data flow)
           Phase 2: pattern analysis (find working examples, diff against them)
           Phase 3: single hypothesis, smallest possible test of it
           Phase 4: fix the root cause (via TDD), verify, and if 3+ fixes have
                     failed, stop and question the architecture instead of
                     trying a 4th fix
      iii. requesting-code-review
           Dispatch an independent reviewer subagent (not the implementer)
           against the task's diff and the plan/spec it was built from.
      iv.  receiving-code-review
           Evaluate feedback technically before acting on it: verify against
           the codebase, implement what's correct, push back with reasoning
           on what's wrong — no reflexive agreement, no blind implementation.
7d. verification-before-completion
      Before ANY "done"/"passing"/"fixed" claim: identify the command that
      proves it, run it fresh in that same turn, read the full output, and
      only then state the claim — with the evidence attached. "Should pass
      now" or trusting a prior run is treated as a false claim.
7e. finishing-a-development-branch
      Run the full test suite on the branch. If green, present the human
      with the integration decision — merge locally, push and open a Pull
      Request, or leave the branch as-is — execute whichever is chosen, then
      clean up the isolated workspace. This is where CI, human review, and
      merge actually happen: they are the outcome of the option chosen here,
      not separate manual steps.
```

If a task turns out not to be independent of others, or the whole feature is being executed in one continuous session rather than parallel subagents, `executing-plans` is used in place of `subagent-driven-development` for step 7b — the per-task discipline in 7c-7d is identical either way. For multiple unrelated failures across independent subsystems (e.g. several unrelated failing test files), `dispatching-parallel-agents` may investigate them concurrently rather than one at a time.

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
