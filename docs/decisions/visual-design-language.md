# Visual Design Language

**Status**: Confirmed Product Owner decision

**Date**: 2026-08-21

**Applies to**: All employee-facing UI. First applied in `specs/003-login-dashboard-ui/`; binding on future picking-workflow UI (order claiming, picking, issue reporting) as those features are specified and planned.

## Purpose

This is the durable visual-design-language decision for Loot Singles Fulfillment's frontend, so each new feature's technical plan does not need to rediscover or re-litigate it. It governs *how things look and feel*; it does not itself introduce product functionality — functional requirements still trace to the PRD and approved Spec Kit feature specifications per `CLAUDE.md`'s source-of-truth hierarchy.

## Principles

1. **Dark mode is the default, not an afterthought.** Use charcoal/slate surfaces rather than pure black everywhere.
2. **Loot green is the main accent** — for primary actions, active states, success, and key highlights. Keep the rest mostly neutral so green feels intentional rather than decorative.
3. **No bright RGB/neon-gaming styling.** The target feel is a modern card shop, not an esports dashboard.
4. **Soft rounded corners, subtle borders, restrained shadows, and layered surfaces**, so the UI feels polished and friendly rather than flat or harsh.
5. **Typography is clean and highly readable.** Headings may carry slightly more personality; operational data (counts, statuses, order details) uses a straightforward sans serif.
6. **Cards and panels may subtly reference trading cards** — through proportions, borders, hover states, or stacked-card treatments — without turning every UI element into a literal trading-card replica.
7. **Fast scanning over decorative density.** An employee should instantly understand what needs action; decoration must never compete with that.
8. **Status color is used intentionally and must stay distinct.** Green must not be reused to mean five different things — claimed, in progress, problem, and completed each get their own distinct treatment, not shades of the same accent.
9. **Mobile and desktop are designed together, not sequentially.** The fulfillment flow must work well both at a workstation and while moving around the store floor (Constitution VIII — one responsive product, not separate designs).
10. **Interactive targets are comfortably usable on a phone** — especially high-frequency actions like Pick Next Order, claim, continue, and report a missing item.
11. **Card images are used as visual identifiers where available**, but the UI must remain fully usable with no loss of clarity when no image exists (consistent with this project's "no image is better than the wrong image" rule — a missing image is an expected, handled state, not a broken one).
12. **Animation is subtle and functional only** — hover, selection, successful claim, state transition. No decorative or excessive motion.

## Brand Mark

The Loot Card Shop logo is a black-line-art d20 (twenty-sided die) merged with a helmet motif, with "20" and "LCS" lettering on the die's facets. A source copy is kept at [`assets/lcs-logo-source.png`](./assets/lcs-logo-source.png) (originally a social-profile picture — black line art on a white/light background, not yet prepared as a production web asset).

Implications for a dark-mode-first UI:

- The source asset is black-stroke-on-light and will read as a solid light box on a charcoal/slate dark surface. It needs a light-stroke (or transparent-background) variant before use anywhere on a dark surface — this is a small asset-prep task for whichever feature first implements branded chrome (e.g., the login screen), not a decision to make now.
- Being geometric line art (a faceted die), it vectorizes cleanly to SVG, which is preferable to the raster source for crisp rendering at both small sizes (favicon, app header) and larger sizes (login screen).
- The die-facet motif is a natural, on-brand link to Principle 6 (trading-card-referencing panels) without being a literal trading card — the faceted/geometric line quality can subtly inform border or divider treatments elsewhere, if a feature's design calls for it.
- Use the mark itself sparingly (e.g., login screen, app header/favicon) rather than as a repeated decorative element, consistent with Principle 7 (fast scanning over decorative density).

## Design Tokens

A small, named token system — not arbitrary colors/spacing scattered per component — is the intended implementation approach for every feature covered by this document. The taxonomy below is the confirmed decision; the values are proposed starting points consistent with the Principles above (dark-mode-first, charcoal/slate, Loot green as the sole brand accent, no neon). They are a reasonable default, not a locked brand-color audit — adjust them if they conflict with an actual Loot Card Shop brand color spec if/when one exists, and treat any such adjustment as a token-value change only, not a retaxonomy.

### Color

| Token | Proposed value (dark theme, default) | Purpose |
|---|---|---|
| Background | `#0F1115` | App background — charcoal, not pure black (Principle 1) |
| Surface | `#171A21` | Default panel/card surface, one step up from Background |
| Elevated Surface | `#1E222B` | Modals, dropdowns, popovers — one step up from Surface, for layering (Principle 4) |
| Border | `#2B303B` | Subtle dividers/outlines — never a hard, high-contrast line |
| Text Primary | `#F2F3F5` | Primary reading text — off-white, not pure white (reduces glare) |
| Text Secondary | `#B4B8C2` | Secondary text, supporting labels |
| Text Muted | `#7A7F8C` | Least-emphasis text — timestamps, placeholders, disabled labels |
| Brand Primary | `#34C77B` ("Loot green") | Primary actions, active states, key highlights (Principle 2) |
| Brand Primary Hover | `#2CA868` | Hover/pressed state for Brand Primary |
| Success | Brand Primary or its own close green | Positive outcomes (e.g., successful claim) — intentionally close to Brand Primary, since "success" and "the brand's primary action color" are naturally related |
| Warning | `#E2A33D` | Needs-attention-soon states — distinct amber, not a shade of green |
| Danger | `#E5484D` | Errors, destructive actions, blocking problems — distinct red |
| Info | `#4C9FE8` | Neutral informational emphasis — distinct blue |

Success sharing the Brand Primary hue is a deliberate exception, not a loophole: Principle 8's rule against green meaning five things is about the future picking-workflow's *status* colors (claimed / in progress / problem / completed, see below) — those MUST NOT reuse Brand Primary green interchangeably with Success. Warning, Danger, and Info must always stay visually distinct from Brand Primary/Success and from each other.

### Radius

| Token | Proposed value | Purpose |
|---|---|---|
| Radius Small | `6px` | Inputs, buttons, badges, chips |
| Radius Medium | `10px` | Cards, panels |
| Radius Large | `16px` | Modals, large containers, hero/feature panels |

### Spacing scale

Base-4 scale: `4, 8, 12, 16, 24, 32, 48, 64` (px) — named `space-1` through `space-8`. Every layout gap, padding, and margin should resolve to one of these values rather than an arbitrary number.

### Typography scale

A small, restrained scale rather than a large arbitrary set of font sizes:

| Token | Proposed size | Use |
|---|---|---|
| Display | 32px | Rare, large marketing-style moments (if any) |
| H1 | 24px | Page/screen title |
| H2 | 18px | Section heading |
| Body | 15px | Default operational text |
| Body Small | 13px | Secondary/supporting text |
| Caption | 12px | Timestamps, labels, least-emphasis text |

Headings may use a typeface with slightly more personality (Principle 5); Body/Body Small/Caption — the operational data sizes — must use a straightforward, highly legible sans serif.

## Notes for future features

Principle 8 (distinct status-color treatment for claimed / in progress / problem / completed) and Principle 10's named actions (Pick Next Order, claim, report a missing item) describe screens that belong to the future order-claiming and picking-workflow features, not `003-login-dashboard-ui`. They are recorded here now so those features' technical plans inherit a consistent status-color and interaction-target system instead of each inventing its own.

When those features are specified, their picking-status colors should map onto the Design Tokens above (e.g., "problem" → Danger or Warning depending on severity, "completed" → Success, "in progress" → Info or a new dedicated token if none of the four fit) rather than introducing new arbitrary colors outside this system. If a genuinely new semantic color is needed, add it here as a new named token rather than hardcoding it in that feature's plan.
