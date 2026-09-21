# Discovery: Picking Experience Prototype and Issue Resolution

**Date**: 2026-09-20
**Participants**: Developer (jkhaynes), with shop owners consulted on label printing
**Addresses**: PRD §42 Priority 4 (Picker Prototype) and Priority 2 (Issue Resolution Workflow)
**Mockups**: <https://claude.ai/artifact/7ZVEWpEPP3gZw93z4A59Ku> (private; share from the page's Share menu)

## Why this session happened

Features 001–015 delivered a working picking loop: import, authenticate, claim, view an order,
confirm each line, report an issue, and see live counts. Using it by hand, the Developer's
assessment was that "the flow just doesn't feel good" — specifically the loop of finishing one
order and starting the next, rather than the mechanics of any single screen.

Rather than specify another feature on top, we stepped back to establish what a finished V1
actually looks like, and prototyped it. That is precisely what PRD §42 Priority 4 asks for, and it
should happen before implementation details become fixed.

## What we learned about the physical workflow

These are operational facts from the shop, not inferences. They drive most of what follows.

**Picked cards are sleeved together and set aside.** Packing happens later, by a different person.
Nothing currently ties a sleeve of cards to the order it belongs to — the app ends its involvement
at "Picked" and the bundle becomes anonymous the moment it leaves the picker's hand. This is the
gap behind the "finishing an order feels unfinished" complaint.

**Label printers are already on site** — a small address-label printer and a larger one — which
moves a printed sticker from aspiration to the V1 baseline. The owners also asked for a barcode in
V1, and left the choice of stock to whatever suits the label.

**Both scanner types will be used** — a handheld scanner at the packing desk normally, a phone
camera as backup. That settles the symbology as QR, since phone cameras read QR reliably and 1D
barcodes mostly not. If the current desk scanner turns out to be a 1D laser it cannot read QR at
all, and the Product Owner has accepted buying a 2D imager rather than changing the symbology.

**Storage is sectioned by game, then ordered newest to oldest.** Pokémon, Magic and Lorcana each
have their own section; within a section, boxes run from the most recent release backwards. The
practical consequence is that crossing between games is the expensive walk, and moving along one
game's aisle is cheap.

**When a card cannot be supplied, the customer decides.** The shop contacts them and offers:
refund the whole order, refund the missing item and ship the rest, or — when a similar card exists
— substitute it with the customer's approval.

**The most common real case is a variant or condition mismatch.** For example: the ordered
non-holo is missing, but an unlisted holo is on the shelf, so the holo was almost certainly entered
into inventory as the non-holo. The picker sets the holo aside **with the order** and a manager
takes it from there. The substitute is therefore identified at pick time and already physically
present — no second trip is needed.

**Customer contact is a manager-only job, done at a desk.** Pickers do the physical work: look
again, set aside a candidate. Managers talk to customers and record what was decided, sitting at a
computer rather than on the floor — so the manager console is a desktop surface, not a mobile one.

**Orders awaiting a customer reply are stored separately** from ready-to-pack bundles — but the
label should still mark them, because physical separation alone has failed before.

## Gaps this exposed in what is already built

| Gap | Consequence |
|---|---|
| `OrderDetailPage` has no claim action | Tapping an order from the dashboard is a dead end; you must back out and use Browse Orders |
| Pressing Pick Next while holding an order returns an error | A state the dashboard should reflect is reported as a failure instead |
| No completion screen (PRD §22) | An order ends with a status label changing and nothing else; no last count before sealing |
| An order in Needs Attention can only leave by someone finding the card | If the card genuinely is not there, the order is stuck forever |
| Picked is terminal | Picked orders accumulate with no notion of what is still on the shelf |
| Lines render in TCGplayer's arbitrary order (PRD §13 unbuilt) | The picker walks between boxes more than necessary — **partly incorrect, see the correction below** |
| Dashboard shows flagged product names inline | Grows without bound as the queue grows |

### Correction, 2026-09-20 (measured after implementation)

The claim that a picker "walks between boxes more than necessary" was an assumption, and
measuring the imported orders in the test environment does not support it.

Counting box visits in the order TCGplayer supplied the lines, against the minimum possible:

| Order | Lines | Box visits as imported | Distinct boxes |
|---|---|---|---|
| 108 | 50 | 28 | 28 |
| 110 | 15 | 15 | 15 |
| 121 | 5 | 5 | 5 |

They are identical. **TCGplayer already groups an order's lines by set**, and in order 108 it
also keeps the two games contiguous. Grouping therefore reduces box visits by zero on this data.

What set-aware picking still delivers is narrower than this document originally claimed:

- **A defined order** rather than TCGplayer's arbitrary one. Order 108 arrives as ME05, ME03,
  ME:, ME02, ME01, SV09 …; alphabetical gives ME:, ME01, ME02, ME03, ME05, then the SV and SWSH
  families in sequence. Pokémon set names encode release order within a family, so alphabetical
  lands close to shelf order at no cost — an accidental argument for having deferred
  release-date ordering.
- **Per-box counts**, so a picker knows what a box owes before opening it.
- **A guarantee rather than a courtesy.** TCGplayer's clustering is not contractual, is not
  something Loot controls, and is not guaranteed across export paths.

This does not invalidate PRD §13, but it does mean the section's stated motivation — reducing
movement between storage boxes — is not the benefit actually being realised today. Worth
confirming against a real pull sheet before §13's rationale is relied on again.

## Decisions taken

| Decision | Relationship to the PRD |
|---|---|
| Focused picking (one card at a time) on phones; list on desktop; both reachable from either | Extends §8, §12, §18 |
| Swiping advances but never records; only an explicit tap records a pick | Confirms §23 |
| Lines group by set, with an explicit "box finished → next box" transition | Implements §13 |
| Sets group by game first, then run alphabetically within the game | Implements §13 |
| Release-date ordering deferred — the catalog does not carry release dates today | Defers part of §13 |
| The manager console is a desktop surface | Narrows §8 |
| An order awaiting a customer shows how long it has waited; no automatic expiry | New |
| A box that still has unpulled cards cannot be left silently; the picker sees what is open and chooses | New guard, follows from §5.6 and §23 |
| Claiming happens on the order screen; Pick Next remains the fast path | Clarifies §10 |
| The dashboard offers "continue picking" when the employee already holds an order | New |
| Every pick ends on a screen: complete, or needs a manager | Implements §22 |
| Both endings print a thermal label carrying a QR deep link, the order id, and card counts | **Contradicts §35's barcode non-goal** |
| A hold label is visibly different (inverted band), never merely a colour | New |
| Orders reach a Packed state through a packing-desk lookup | **New lifecycle state beyond §20** |
| A picking issue can carry the card found instead, so a manager reviews a proposal | Extends §19.1 |
| Manager-only: customer contact and recording refund / ship-short / substitute | New |
| Needs Attention splits into "needs a decision" and "awaiting customer" | New |
| Dashboard shows counts only; details live on their own pages | Revises feature 015's FR-014 surface |

## Set ordering — decided, and partly deferred

Storage is sectioned by game and ordered newest-to-oldest within a section, so the ideal sort is
game first, then release date descending.

Only the first half of that is affordable today. `OrderLine.ProductLine` carries the game
("Pokemon", "Magic", "Lorcana TCG") straight from the packing slip, so it is authoritative order
data and grouping by it is free. Release dates are not held anywhere: `OrderLine.Set` is a name
string, and the three set catalogs
(`TcgdexSetCatalog.cs:80`, `LorcastSetCatalog.cs:113`, and the Scryfall provider) deserialize only
an id and a name, because they exist to resolve card images rather than to describe sets. The
upstream APIs do publish release dates, so this is obtainable — it means widening those fetches
per game and defining where an undated set sorts.

The decision is to group by game and sort alphabetically within each game for now. Crossing
between game sections is the expensive walk and that is what grouping fixes; ordering within one
game's aisle only decides which direction the picker walks it. Release ordering can be added later
without disturbing anything built on this rule.

Unlike an uncertain image, an imperfect sort is not a safety problem — it costs a few steps, and
nothing about it claims to be authoritative. The "no image is better than the wrong image" rule
does not transfer here.

## Label stock — decided

The small address-label printer, on standard address stock (about 1⅛ × 3½ inches). The larger
printer's shipping stock is bigger than the sleeve the label goes on. An address label suits a
sleeve's top edge and holds everything the label needs: a one-inch QR at the left, with the order
code, counts, picker and time stacked beside it. The hold variant carries an inverted band along
one edge, which reads at a glance without relying on colour.

Printing goes through the browser's print path from whichever device the picker is holding. The
Azure-hosted API cannot reach a printer on the shop LAN, so printing is necessarily client-side.

## Still open

- **Browser printing has not been tried against the real printer.** Print scaling, margins and
  driver behaviour decide whether a browser-generated label comes out the right physical size.
  This should be proven on the actual hardware early in the hand-off feature, not discovered late.
- **None of this has been validated with the people who pull Loot orders.** The prototype reflects
  one developer's use of the app plus what the owners described. PRD §42 Priority 4 asks for
  validation, and watching a picker work through a real order would still be worth doing before or
  alongside implementation.

## Proposed decomposition

Three features, each independently shippable:

1. **Mobile picking experience** — focused mode, set grouping, box transitions and the skip guard,
   claim-on-order, claim-aware dashboard. Mostly frontend plus one small API addition. No PRD
   conflicts: it builds what §12, §13 and §18 already require.
2. **Pick completion and hand-off** — both ending screens, label printing, the awaiting-packing
   queue, the Packed state, the packing desk. Requires the PRD amendment and a printer on site.
3. **Issue resolution** — manager console, awaiting-customer state, substitute capture, ship-short,
   cancellation, hold labels. Largest of the three, and depends on the lifecycle states the second
   introduces.

Suggested order is as listed, on the grounds that the first delivers the largest felt improvement
at the lowest risk. If sleeves are actually being confused at the bench today, the second should go
first: that is an operational hole rather than an ergonomic one.
