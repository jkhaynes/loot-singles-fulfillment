Perform the Code and Design Review gate for the current Spec Kit feature.

This is a REVIEW ONLY pass. Do not modify production code, tests, specs,
plans, or tasks yet.

Read:
- the project constitution
- the current feature's spec.md
- plan.md
- tasks.md
- the implementation diff against the feature's base branch
- all changed source and test files
- directly related collaborators where necessary to understand the design

Review the implementation for:

1. Correctness against the approved specification
2. Compliance with the approved technical plan
3. Compliance with the constitution
4. SOLID principles where materially applicable
5. Separation of concerns and focused responsibilities
6. Dependency direction and architectural boundaries
7. Typed and explicit modeling of expected failures
8. Domain integrity and prevention of invalid states
9. Extensibility at variation points actually implied by requirements
10. Central conditional/orchestration code that must be repeatedly modified
11. Duplication of domain or validation behavior
12. Leakage of source-specific or infrastructure-specific behavior
13. Test quality and meaningful behavioral coverage
14. Security, privacy, concurrency, and safe-failure concerns where applicable
15. Unnecessary abstractions or speculative overengineering

Do not recommend abstraction merely because a design pattern could apply.

Classify every finding as:

MUST FIX:
A concrete correctness, specification, plan, constitution, security, privacy,
testing, or material maintainability violation.

ADVISORY:
An optional improvement, subjective preference, or speculative optimization.

For each MUST FIX finding provide:
- affected file(s)
- concrete evidence
- violated requirement/plan/constitution principle
- why it matters
- the smallest appropriate remediation direction

If the root problem is in the technical plan, say PLAN CHANGE REQUIRED rather
than proposing an implementation-only patch.

If the root problem is an unresolved requirement, say CLARIFICATION REQUIRED.

Do not edit anything.

Finish with:
- Must Fix count
- Advisory count
- whether the feature is ready for /speckit.converge
- whether work should return to clarify, plan, tasks/implement, or proceed