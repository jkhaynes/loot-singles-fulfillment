import type { OrderLineDetail } from './ordersApi'

/**
 * Grouping, ordering and progress for a picked order (016-mobile-picking, PRD §13, §18).
 *
 * Deliberately a module of pure functions rather than logic inside a component. Two reasons,
 * both concrete: a module that cannot issue a request makes FR-011 — navigation never records an
 * outcome — structural rather than a matter of discipline, and the most intricate logic in the
 * feature can then be tested without rendering anything.
 *
 * Everything here derives from authoritative imported order data (`productLine`, `set`,
 * `quantity`, `pickOutcome`). Catalog enrichment is never consulted (FR-006, PRD §33).
 */

/** One storage box's worth of an order. */
export interface SetGroup {
  /** The game, from the order line. Loot stores each game in its own section. */
  game: string
  /** The set name as imported, or a placeholder when none was recorded. */
  setName: string
  /** False when the line's set was missing or blank — the group is labelled, never dropped. */
  isSetRecorded: boolean
  lines: OrderLineDetail[]
  productCount: number
  /** Physical cards, summing quantity. A line of three contributes three. */
  cardCount: number
  resolvedProductCount: number
  unresolvedLines: OrderLineDetail[]
  isComplete: boolean
}

export interface OrderProgress {
  resolvedProducts: number
  totalProducts: number
  accountedCards: number
  totalCards: number
  currentSetName: string
  currentSetPosition: number
  currentSetSize: number
}

/** Shown in place of a set name when the import recorded none. */
export const UNRECORDED_SET_LABEL = 'Set not recorded'

/** A line is resolved once it carries any outcome — picked, or flagged with an issue. */
export function isResolved(line: OrderLineDetail): boolean {
  return line.pickOutcome !== null
}

/**
 * Orders two sets within the same game.
 *
 * Kept as its own named function because PRD §13.1 explicitly anticipates release-date ordering
 * replacing alphabetical once set release dates are available — swapping this one comparator is
 * then the whole change. Deliberately not an injected strategy: there is one implementation
 * today, and an interface with a single implementation is the over-abstraction the constitution
 * warns against.
 */
export function compareSets(a: SetGroup, b: SetGroup): number {
  // A group with no recorded set sorts after every named set of its game, so the picker walks
  // real boxes first and deals with the oddity last.
  if (a.isSetRecorded !== b.isSetRecorded) return a.isSetRecorded ? -1 : 1

  const byName = a.setName.localeCompare(b.setName, undefined, { sensitivity: 'accent' })
  if (byName !== 0) return byName

  // Names differing only by case: fall back to a raw comparison so the order is total and
  // repeatable rather than depending on sort stability.
  return a.setName < b.setName ? -1 : a.setName > b.setName ? 1 : 0
}

function compareGames(a: SetGroup, b: SetGroup): number {
  const byName = a.game.localeCompare(b.game, undefined, { sensitivity: 'accent' })
  if (byName !== 0) return byName

  return a.game < b.game ? -1 : a.game > b.game ? 1 : 0
}

/**
 * Groups an order's lines into the boxes a picker walks: by game, then by set.
 *
 * Total by construction — every input line lands in exactly one group, whatever its set value.
 * Losing a line here would hide work the picker must do, which is the class of silent failure
 * the constitution forbids (Principle V).
 */
export function groupOrderLines(lines: OrderLineDetail[]): SetGroup[] {
  const groups = new Map<string, SetGroup>()

  for (const line of lines) {
    const setName = line.set.trim()
    const isSetRecorded = setName.length > 0
    // Keyed on the raw set name so two sets differing only by case stay distinct boxes.
    const key = `${line.productLine}\u0000${isSetRecorded ? setName : ''}`

    let group = groups.get(key)
    if (!group) {
      group = {
        game: line.productLine,
        setName: isSetRecorded ? setName : UNRECORDED_SET_LABEL,
        isSetRecorded,
        lines: [],
        productCount: 0,
        cardCount: 0,
        resolvedProductCount: 0,
        unresolvedLines: [],
        isComplete: true,
      }
      groups.set(key, group)
    }

    // Lines keep the relative order the import gave them; only sets are reordered.
    group.lines.push(line)
    group.productCount += 1
    group.cardCount += line.quantity

    if (isResolved(line)) {
      group.resolvedProductCount += 1
    } else {
      group.unresolvedLines.push(line)
    }
  }

  for (const group of groups.values()) {
    // Never complete while anything in it is outstanding (FR-018).
    group.isComplete = group.unresolvedLines.length === 0
  }

  return [...groups.values()].sort((a, b) => compareGames(a, b) || compareSets(a, b))
}

/**
 * What lies past the line the picker just finished with.
 *
 * Returned as data rather than acted upon, so the decision of which screen to show stays with
 * the component and this module keeps its one job. Crucially, asking what comes next records
 * nothing — advancing is a question, not an outcome (FR-011).
 */
export type Advance =
  /** The next product. `enteringSet` is set when it belongs to a different box (FR-015). */
  | { kind: 'line'; line: OrderLineDetail; enteringSet: SetGroup | null }
  /** Past the last product in the order. */
  | { kind: 'order-end' }

/**
 * Where advancing from `fromLineId` leads.
 *
 * **Never blocks** (FR-019a). Moving between products is looking, not deciding, so nothing
 * outstanding can stop it — the same principle as FR-011, applied to navigation rather than
 * recording. An earlier version stopped the picker at every box boundary with work outstanding;
 * boxes holding a single card turned out to be the common case, so that fired on nearly every
 * card and made the order impossible to browse.
 *
 * The check that an order is not finished short now lives in `unresolvedAcrossOrder`, at the
 * point where finishing is actually attempted.
 */
export function advanceFrom(groups: SetGroup[], fromLineId: number): Advance {
  const ordered = groups.flatMap((group) => group.lines.map((line) => ({ line, group })))
  const index = ordered.findIndex((entry) => entry.line.id === fromLineId)
  if (index === -1 || index === ordered.length - 1) return { kind: 'order-end' }

  const current = ordered[index]
  const next = ordered[index + 1]

  return {
    kind: 'line',
    line: next.line,
    // Named only on the step that actually crosses into a new box, so the card can announce it
    // without a screen of its own (FR-015).
    enteringSet: next.group === current.group ? null : next.group,
  }
}

/** Every product still unresolved, in walking order. Drives the finish check (FR-016). */
export function unresolvedAcrossOrder(groups: SetGroup[]): OrderLineDetail[] {
  return groups.flatMap((group) => group.unresolvedLines)
}

/** The box a product belongs to, for the band shown on its card. */
export function setGroupOf(groups: SetGroup[], lineId: number): SetGroup | null {
  return groups.find((group) => group.lines.some((line) => line.id === lineId)) ?? null
}

/**
 * Progress for the picker: how much of the order is done, and where they are standing.
 *
 * "Accounted for" means resolved, not successfully found — a picker who could not find two of
 * three copies has still dealt with that product.
 */
export function computeProgress(groups: SetGroup[], currentLineId: number | null): OrderProgress {
  let resolvedProducts = 0
  let totalProducts = 0
  let accountedCards = 0
  let totalCards = 0

  for (const group of groups) {
    totalProducts += group.productCount
    totalCards += group.cardCount
    resolvedProducts += group.resolvedProductCount

    for (const line of group.lines) {
      if (isResolved(line)) accountedCards += line.quantity
    }
  }

  const currentGroup =
    groups.find((group) => group.lines.some((line) => line.id === currentLineId)) ?? groups[0]

  const currentSetPosition = currentGroup
    ? currentGroup.lines.findIndex((line) => line.id === currentLineId) + 1
    : 0

  return {
    resolvedProducts,
    totalProducts,
    accountedCards,
    totalCards,
    currentSetName: currentGroup?.setName ?? '',
    // Position is reported within the set, not the order (FR-022) — it answers "how much of
    // this box is left?", which is the question the picker standing at it actually has.
    currentSetPosition: Math.max(currentSetPosition, currentGroup ? 1 : 0),
    currentSetSize: currentGroup?.lines.length ?? 0,
  }
}
