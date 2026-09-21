import { beforeEach, describe, expect, it } from 'vitest'
import {
  advanceFrom,
  computeProgress,
  groupOrderLines,
  setGroupOf,
  unresolvedAcrossOrder,
} from '../../src/features/orders/orderGrouping'
import {
  buildIssueLine,
  buildLine,
  buildMultiGameLines,
  resetLineIds,
} from '../support/orderBuilders'

beforeEach(() => {
  resetLineIds()
})

/** Every line id across every group, in presentation order. */
function groupedLineIds(lines: ReturnType<typeof buildLine>[]): number[] {
  return groupOrderLines(lines).flatMap((group) => group.lines.map((line) => line.id))
}

describe('groupOrderLines — totality (T004)', () => {
  it('returns no groups for an empty order', () => {
    expect(groupOrderLines([])).toEqual([])
  })

  it('keeps the single line of a one-line order', () => {
    const line = buildLine({ productName: 'Pikachu ex' })

    const groups = groupOrderLines([line])

    expect(groups).toHaveLength(1)
    expect(groups[0].lines).toEqual([line])
  })

  it('places every line in exactly one group', () => {
    const lines = buildMultiGameLines()

    const ids = groupedLineIds(lines)

    // A permutation of the input: nothing dropped, nothing duplicated. This is the invariant a
    // grouping bug would break, and the one that would lose a picker's work.
    expect([...ids].sort((a, b) => a - b)).toEqual(
      lines.map((line) => line.id).sort((a, b) => a - b),
    )
    expect(new Set(ids).size).toBe(lines.length)
  })

  it('is deterministic', () => {
    const lines = buildMultiGameLines()

    expect(groupedLineIds(lines)).toEqual(groupedLineIds(lines))
  })
})

describe('groupOrderLines — ordering (T005)', () => {
  it('orders games alphabetically by name', () => {
    const groups = groupOrderLines(buildMultiGameLines())

    // Magic before Pokemon, whatever order the import supplied them in.
    expect(groups.map((group) => group.game)).toEqual(['Magic', 'Magic', 'Pokemon', 'Pokemon'])
  })

  it('orders sets alphabetically within a game', () => {
    const groups = groupOrderLines(buildMultiGameLines())

    expect(groups.map((group) => group.setName)).toEqual([
      'Aetherdrift',
      'Bloomburrow',
      'Black Bolt',
      'Surging Sparks',
    ])
  })

  it('keeps every line of a set contiguous', () => {
    const groups = groupOrderLines(buildMultiGameLines())
    const surgingSparks = groups.find((group) => group.setName === 'Surging Sparks')

    expect(surgingSparks?.lines.map((line) => line.productName)).toEqual([
      'Pikachu ex',
      'Latias ex',
    ])
  })

  it('preserves the relative order of lines within a set', () => {
    const lines = [
      buildLine({ set: 'Base Set', productName: 'Third' }),
      buildLine({ set: 'Base Set', productName: 'First' }),
      buildLine({ set: 'Base Set', productName: 'Second' }),
    ]

    const groups = groupOrderLines(lines)

    // Sets are reordered; lines inside a set are not.
    expect(groups[0].lines.map((line) => line.productName)).toEqual(['Third', 'First', 'Second'])
  })

  it('orders set names that differ only by case deterministically', () => {
    // Imported data is not case-normalised, so this must be total and repeatable rather than
    // depending on sort stability.
    const lines = [
      buildLine({ set: 'base set', productName: 'Lowercase' }),
      buildLine({ set: 'Base Set', productName: 'Titlecase' }),
      buildLine({ set: 'Alpha', productName: 'Earlier' }),
    ]

    const first = groupOrderLines(lines).map((group) => group.setName)
    const second = groupOrderLines(lines).map((group) => group.setName)

    expect(first).toEqual(second)
    expect(first[0]).toBe('Alpha')
    expect(first.slice(1).sort()).toEqual(['Base Set', 'base set'])
  })

  it('reports per-group product and physical card counts', () => {
    const groups = groupOrderLines(buildMultiGameLines())
    const blackBolt = groups.find((group) => group.setName === 'Black Bolt')

    expect(blackBolt?.productCount).toBe(1)
    // One product line, three physical cards.
    expect(blackBolt?.cardCount).toBe(3)
  })
})

describe('groupOrderLines — same set name in different games (T006)', () => {
  it('does not merge sets that share a name across games', () => {
    const lines = [
      buildLine({ productLine: 'Magic', set: 'Promo', productName: 'Magic Promo' }),
      buildLine({ productLine: 'Pokemon', set: 'Promo', productName: 'Pokemon Promo' }),
    ]

    const groups = groupOrderLines(lines)

    // Two boxes in two sections of the shop, not one box.
    expect(groups).toHaveLength(2)
    expect(groups.map((group) => group.game)).toEqual(['Magic', 'Pokemon'])
    expect(groups.every((group) => group.lines.length === 1)).toBe(true)
  })
})

describe('groupOrderLines — missing set values (T007)', () => {
  it.each([
    ['empty', ''],
    ['whitespace only', '   '],
  ])('keeps a line whose set is %s', (_label, set) => {
    const lines = [
      buildLine({ productLine: 'Pokemon', set: 'Base Set', productName: 'Pikachu' }),
      buildLine({ productLine: 'Pokemon', set, productName: 'Mystery Promo' }),
    ]

    const groups = groupOrderLines(lines)

    const allLines = groups.flatMap((group) => group.lines)
    expect(allLines).toHaveLength(2)
    expect(allLines.map((line) => line.productName)).toContain('Mystery Promo')
  })

  it('marks a group with no recorded set', () => {
    const groups = groupOrderLines([buildLine({ set: '' })])

    expect(groups[0].isSetRecorded).toBe(false)
  })

  it('sorts a group with no recorded set last within its game', () => {
    const lines = [
      buildLine({ productLine: 'Pokemon', set: '', productName: 'Mystery Promo' }),
      buildLine({ productLine: 'Pokemon', set: 'Zephyr', productName: 'Late Alphabetically' }),
      buildLine({ productLine: 'Pokemon', set: 'Base Set', productName: 'Pikachu' }),
    ]

    const groups = groupOrderLines(lines)

    // Last within Pokemon even though "" would sort first as a plain string.
    expect(groups.map((group) => group.isSetRecorded)).toEqual([true, true, false])
  })

  it('keeps unrecorded groups inside their own game, not at the end of the order', () => {
    const lines = [
      buildLine({ productLine: 'Magic', set: '', productName: 'Magic Unknown' }),
      buildLine({ productLine: 'Pokemon', set: 'Base Set', productName: 'Pikachu' }),
    ]

    const groups = groupOrderLines(lines)

    // The unrecorded Magic group belongs with Magic, before Pokemon begins.
    expect(groups.map((group) => group.game)).toEqual(['Magic', 'Pokemon'])
  })
})

describe('computeProgress (T008)', () => {
  it('reports nothing resolved for a fresh order', () => {
    const groups = groupOrderLines(buildMultiGameLines())

    const progress = computeProgress(groups, null)

    expect(progress.resolvedProducts).toBe(0)
    expect(progress.totalProducts).toBe(5)
    expect(progress.accountedCards).toBe(0)
    // Five lines, seven physical cards — the line of three is why these differ.
    expect(progress.totalCards).toBe(7)
  })

  it('counts physical cards rather than product lines', () => {
    const lines = [buildLine({ quantity: 3, pickOutcome: 'picked' }), buildLine({ quantity: 1 })]

    const progress = computeProgress(groupOrderLines(lines), null)

    expect(progress.resolvedProducts).toBe(1)
    expect(progress.accountedCards).toBe(3)
    expect(progress.totalCards).toBe(4)
  })

  it('counts a line carrying an issue as accounted for', () => {
    // "Accounted for" means resolved, not successfully found — a picker who cannot find a card
    // has still dealt with it.
    const lines = [buildIssueLine({ quantity: 2 }), buildLine({ quantity: 1 })]

    const progress = computeProgress(groupOrderLines(lines), null)

    expect(progress.resolvedProducts).toBe(1)
    expect(progress.accountedCards).toBe(2)
  })

  it('reports everything resolved once every line has an outcome', () => {
    const lines = [
      buildLine({ quantity: 2, pickOutcome: 'picked' }),
      buildIssueLine({ quantity: 1 }),
    ]

    const progress = computeProgress(groupOrderLines(lines), null)

    expect(progress.resolvedProducts).toBe(progress.totalProducts)
    expect(progress.accountedCards).toBe(progress.totalCards)
  })

  it('reports position within the current set, not within the order', () => {
    const lines = buildMultiGameLines()
    const groups = groupOrderLines(lines)
    const surgingSparks = groups.find((group) => group.setName === 'Surging Sparks')!
    const secondLineOfThatSet = surgingSparks.lines[1]

    const progress = computeProgress(groups, secondLineOfThatSet.id)

    expect(progress.currentSetName).toBe('Surging Sparks')
    expect(progress.currentSetPosition).toBe(2)
    expect(progress.currentSetSize).toBe(2)
  })

  // BR-002. The whole-order view passes null — it has no current card — and used to be told it
  // was standing at position 1 of the first box. Nothing rendered that number, so the lie was
  // invisible; the next caller to read it would have been the one to find out.
  it('reports no position when there is no current line', () => {
    const groups = groupOrderLines(buildMultiGameLines())

    const progress = computeProgress(groups, null)

    expect(progress.currentSetPosition).toBe(0)
    expect(progress.currentSetName).toBe('')
    // The order-wide counts are still real: they do not depend on where the picker is.
    expect(progress.totalProducts).toBe(5)
    expect(progress.totalCards).toBe(7)
  })

  it('reports no position for a line that is not in the order', () => {
    const groups = groupOrderLines(buildMultiGameLines())

    const progress = computeProgress(groups, 999_999)

    expect(progress.currentSetPosition).toBe(0)
    expect(progress.currentSetName).toBe('')
  })
})

describe('advanceFrom — never blocks (FR-019a)', () => {
  function twoBoxes() {
    return [
      buildLine({ set: 'Alpha', productName: 'A1' }),
      buildLine({ set: 'Alpha', productName: 'A2' }),
      buildLine({ set: 'Beta', productName: 'B1' }),
    ]
  }

  it('moves to the next product in the same box', () => {
    const lines = twoBoxes()
    const result = advanceFrom(groupOrderLines(lines), lines[0].id)

    expect(result.kind).toBe('line')
    expect(result.kind === 'line' && result.line.productName).toBe('A2')
    expect(result.kind === 'line' && result.enteringSet).toBeNull()
  })

  it('crosses into the next box without stopping, naming it', () => {
    const lines = twoBoxes()
    const result = advanceFrom(groupOrderLines(lines), lines[1].id)

    expect(result.kind).toBe('line')
    expect(result.kind === 'line' && result.line.productName).toBe('B1')
    // Named so the card can announce the new box, rather than a screen of its own (FR-015).
    expect(result.kind === 'line' && result.enteringSet?.setName).toBe('Beta')
  })

  it('crosses a box boundary even with the box left unresolved', () => {
    // This is the whole point of the change. An earlier build stopped here, and because boxes
    // holding one card are the common case it fired on nearly every card.
    const lines = twoBoxes()
    const result = advanceFrom(groupOrderLines(lines), lines[1].id)

    expect(result.kind).toBe('line')
  })

  it('never blocks on a box of exactly one unresolved card', () => {
    const lines = [
      buildLine({ set: 'Alpha', productName: 'Only One' }),
      buildLine({ set: 'Beta', productName: 'Next Box' }),
    ]

    const result = advanceFrom(groupOrderLines(lines), lines[0].id)

    expect(result.kind).toBe('line')
    expect(result.kind === 'line' && result.line.productName).toBe('Next Box')
  })

  it('walks an order of single-card boxes end to end without one interruption', () => {
    const lines = ['A', 'B', 'C', 'D', 'E'].map((s) =>
      buildLine({ set: s, productName: `Card ${s}` }),
    )
    const groups = groupOrderLines(lines)

    const visited: string[] = ['Card A']
    let id = lines[0].id
    for (;;) {
      const step = advanceFrom(groups, id)
      if (step.kind !== 'line') break
      visited.push(step.line.productName)
      id = step.line.id
    }

    expect(visited).toEqual(['Card A', 'Card B', 'Card C', 'Card D', 'Card E'])
  })

  it('reports the end of the order past the last product', () => {
    const lines = twoBoxes()
    expect(advanceFrom(groupOrderLines(lines), lines[2].id).kind).toBe('order-end')
  })
})

describe('unresolvedAcrossOrder', () => {
  it('collects what is outstanding across every box, in walking order', () => {
    const lines = [
      buildLine({ set: 'Alpha', productName: 'A1', pickOutcome: 'picked' }),
      buildLine({ set: 'Alpha', productName: 'A2' }),
      buildLine({ set: 'Beta', productName: 'B1' }),
      buildIssueLine({ set: 'Beta', productName: 'B2' }),
    ]

    const outstanding = unresolvedAcrossOrder(groupOrderLines(lines))

    // Picked and issue-reported lines are both resolved; only the untouched two remain.
    expect(outstanding.map((line) => line.productName)).toEqual(['A2', 'B1'])
  })

  it('is empty once every product has an outcome', () => {
    const lines = [
      buildLine({ set: 'Alpha', pickOutcome: 'picked' }),
      buildIssueLine({ set: 'Beta' }),
    ]

    expect(unresolvedAcrossOrder(groupOrderLines(lines))).toEqual([])
  })
})

describe('setGroupOf', () => {
  it('finds the box a product belongs to', () => {
    const lines = [buildLine({ set: 'Alpha' }), buildLine({ set: 'Beta' })]
    const groups = groupOrderLines(lines)

    expect(setGroupOf(groups, lines[1].id)?.setName).toBe('Beta')
  })

  it('returns null for a product that is not in the order', () => {
    expect(setGroupOf(groupOrderLines([buildLine()]), 9999)).toBeNull()
  })
})
